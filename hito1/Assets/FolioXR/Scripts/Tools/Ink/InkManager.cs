using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FolioXR.Environments;
using FolioXR.Reading;
using UnityEngine;

namespace FolioXR.Tools.Ink
{
    internal struct InkContact
    {
        internal InkCanvas Canvas;
        internal Vector3 Point;   // Pie de la punta sobre la superficie (mundo, sin el desplazamiento de 1 mm).
        internal Vector3 Normal;  // Normal de la superficie hacia el lápiz.
        internal float Distance;  // Distancia de la punta a la superficie (negativa si la atraviesa).
    }

    /// <summary>Trazo en curso: su propia malla, que crece de a un par de vértices por punto.</summary>
    internal sealed class LiveStroke
    {
        internal InkCanvas Canvas;
        internal InkStroke Stroke;
        internal Mesh Mesh;
        internal GameObject Object;
        internal readonly List<Vector3> Vertices = new List<Vector3>(InkGeometry.MaxPoints * 2);
        internal readonly List<int> Triangles = new List<int>(InkGeometry.MaxPoints * 6);
        internal bool Full => Stroke.Points.Count >= InkGeometry.MaxPoints;
    }

    /// <summary>
    /// Tanda D (escrita por Claude). Lienzos, contacto con superficies, goma, guardado atómico en JSON
    /// y revelado de la tinta invisible. Lo crea el registro de herramientas.
    /// </summary>
    [DefaultExecutionOrder(20030)] // Después del rig y del libro: las anclas de página siguen su LateUpdate.
    public sealed class InkManager : MonoBehaviour
    {
        public static InkManager Instance { get; private set; }

        private const float ContactDistance = 0.005f, MaxPenetration = 0.03f, ProbeBack = 0.15f;
        private const float MinSpacing = 0.0015f, SaveDelay = 1.5f, RevealInterval = 0.1f, RebuildInterval = 0.1f;

        internal Material[] Materials { get; private set; }
        internal Material RevealMaterial { get; private set; }

        private readonly Dictionary<string, InkCanvas> _persistent = new Dictionary<string, InkCanvas>();
        private readonly Dictionary<Transform, InkCanvas> _temporary = new Dictionary<Transform, InkCanvas>();
        private readonly List<InkCanvas> _scratch = new List<InkCanvas>();
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Collider[] _overlaps = new Collider[16];
        private readonly object _writeLock = new object();
        private Task _writes = Task.CompletedTask;
        private string _folder;

        private FolioBook _book;
        private FolioEnvironmentController _environment;
        private Transform _rigRoot;
        private InkCanvas _leftCanvas, _rightCanvas, _houseCanvas;
        private Transform _houseRoot;
        private string _shownDocument;
        private int _shownLeft = -2, _shownRightPage = -2;
        private bool _shownRight;
        private float _nextReveal, _nextRebuild, _nextEnvironmentFind;
        private bool _initialized;

        internal void Initialize(ToolVisuals visuals, FolioBook book, Transform rigRoot)
        {
            Instance = this;
            _book = book;
            _rigRoot = rigRoot;
            _environment = FindFirstObjectByType<FolioEnvironmentController>();
            Materials = new Material[InkPalette.Count];
            for (int i = 0; i < InkPalette.Count; i++)
                Materials[i] = i == InkPalette.Highlighter ? visuals.Transparent("tinta " + InkPalette.Names[i], InkPalette.Colors[i])
                                                           : visuals.Solid("tinta " + InkPalette.Names[i], InkPalette.Colors[i]);
            RevealMaterial = visuals.Solid("tinta invisible revelada", InkPalette.Revealed);
            _folder = Path.Combine(Application.persistentDataPath, "trazos");
            try
            {
                Directory.CreateDirectory(_folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Trazos: no se pudo crear la carpeta " + _folder + ": " + e.Message);
            }
            if (_book != null && _book.bookCollider != null)
            {
                InkSurface surface = _book.bookCollider.gameObject.GetComponent<InkSurface>();
                if (surface == null)
                    surface = _book.bookCollider.gameObject.AddComponent<InkSurface>();
                surface.Resolve = ResolveBook;
            }
            _initialized = true;
            Debug.Log("[FolioXR] Trazos: listos en " + _folder);
        }

        // ───────────────────────── Contacto ─────────────────────────

        /// <summary>Busca la superficie que toca la punta (≤ 5 mm). Nunca en el aire.</summary>
        internal bool Probe(Vector3 tip, Vector3 axis, Transform ignore, out InkContact contact)
        {
            contact = default;
            if (!_initialized)
                return false;
            bool found = false;
            float best = float.PositiveInfinity;
            // Desde 15 cm atrás: el libro a 5x es una caja gruesa y la punta puede quedar dentro de ella.
            int count = Physics.RaycastNonAlloc(tip - axis * ProbeBack, axis, _hits, ProbeBack + ContactDistance + 0.001f,
                                                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (!Usable(hit.collider, ignore) || Vector3.Dot(hit.normal, axis) > -0.1f)
                    continue;
                if (!TryResolve(hit.collider, tip, hit.point, hit.normal, out InkContact candidate))
                    continue;
                float score = Mathf.Abs(candidate.Distance);
                if (score >= best)
                    continue;
                best = score;
                contact = candidate;
                found = true;
            }
            if (found)
                return true;
            // Lápiz casi paralelo a la superficie: punto más cercano de colisionadores simples.
            count = Physics.OverlapSphereNonAlloc(tip, ContactDistance + 0.001f, _overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlaps[i];
                if (!Usable(collider, ignore) || !SupportsClosestPoint(collider))
                    continue;
                Vector3 closest = collider.ClosestPoint(tip);
                Vector3 delta = tip - closest;
                float distance = delta.magnitude;
                if (distance < 1e-5f || distance >= best)
                    continue;
                if (!TryResolve(collider, tip, closest, delta / distance, out InkContact candidate))
                    continue;
                best = distance;
                contact = candidate;
                found = true;
            }
            return found;
        }

        private bool Usable(Collider collider, Transform ignore)
        {
            if (collider == null || !collider.enabled || collider is CharacterController)
                return false;
            if (ignore != null && collider.transform.IsChildOf(ignore))
                return false;
            bool surface = collider.GetComponent<InkSurface>() != null;
            if (collider.isTrigger && !surface)
                return false;
            // Manos, mandos y herramientas sostenidas cuelgan del rig: no son superficies (salvo la libreta).
            return surface || _rigRoot == null || !collider.transform.IsChildOf(_rigRoot);
        }

        private static bool SupportsClosestPoint(Collider collider) =>
            collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
            (collider is MeshCollider mesh && mesh.convex);

        private bool TryResolve(Collider collider, Vector3 tip, Vector3 point, Vector3 normal, out InkContact contact)
        {
            contact = default;
            InkCanvas canvas;
            Vector3 surfacePoint, surfaceNormal;
            InkSurface surface = collider.GetComponent<InkSurface>();
            if (surface != null && surface.Resolve != null)
            {
                if (!surface.Resolve(tip, point, normal, out canvas, out surfacePoint, out surfaceNormal))
                    return false;
            }
            else
            {
                InkCanvas house = HouseCanvas();
                canvas = house != null && _houseRoot != null && collider.transform.IsChildOf(_houseRoot)
                             ? house
                             : Temporary(collider.transform);
                surfaceNormal = normal;
                surfacePoint = tip - normal * Vector3.Dot(tip - point, normal);
            }
            if (canvas == null || canvas.Anchor == null)
                return false;
            float distance = Vector3.Dot(tip - surfacePoint, surfaceNormal);
            if (distance > ContactDistance || distance < -MaxPenetration)
                return false;
            contact = new InkContact { Canvas = canvas, Point = surfacePoint, Normal = surfaceNormal, Distance = distance };
            return true;
        }

        // ───────────────────────── Lienzos ─────────────────────────

        private bool ResolveBook(Vector3 tip, Vector3 hitPoint, Vector3 hitNormal, out InkCanvas canvas,
                                 out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            for (int side = 0; side < 2; side++)
            {
                Renderer page = side == 0 ? _book.leftPage : _book.rightPage;
                InkCanvas pageCanvas = side == 0 ? _leftCanvas : _rightCanvas;
                if (page == null || !page.enabled || pageCanvas == null || pageCanvas.Anchor == null)
                    continue;
                Transform t = page.transform;
                Vector3 local = t.InverseTransformPoint(tip);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
                    continue;
                Vector3 n = -t.forward; // Las páginas miran hacia -Z del libro.
                if (Vector3.Dot(hitNormal, n) < 0.5f)
                    continue;
                canvas = pageCanvas;
                surfaceNormal = n;
                surfacePoint = tip - n * Vector3.Dot(tip - t.position, n);
                return true;
            }
            // Tapa y cantos: se escribe, pero no se guarda (no pertenece a ninguna página).
            canvas = Temporary(_book.transform);
            surfaceNormal = hitNormal;
            surfacePoint = tip - hitNormal * Vector3.Dot(tip - hitPoint, hitNormal);
            return canvas != null;
        }

        /// <summary>Lienzo guardado. Si su ancla se destruyó (casa rehecha), conserva los trazos en una nueva.</summary>
        internal InkCanvas GetPersistent(string key, Func<Transform> createAnchor, Collider support, bool ownsAnchor)
        {
            if (_persistent.TryGetValue(key, out InkCanvas existing))
            {
                if (existing.Anchor != null)
                    return existing;
                var moved = new InkCanvas(this, key, createAnchor(), support) { OwnsAnchor = ownsAnchor };
                moved.Strokes.AddRange(existing.Strokes);
                moved.Strokes.RemoveAll(s => s.Live);
                moved.SaveDue = existing.SaveDue;
                existing.Dispose();
                moved.Rebuild();
                _persistent[key] = moved;
                return moved;
            }
            var canvas = new InkCanvas(this, key, createAnchor(), support) { OwnsAnchor = ownsAnchor };
            Load(canvas);
            _persistent[key] = canvas;
            return canvas;
        }

        private InkCanvas Temporary(Transform owner)
        {
            if (owner == null)
                return null;
            if (_temporary.TryGetValue(owner, out InkCanvas canvas) && canvas.Anchor != null)
                return canvas;
            Vector3 s = owner.lossyScale;
            bool uniform = Mathf.Abs(s.x) > 1e-4f && Mathf.Abs(s.x - s.y) < 0.02f * Mathf.Abs(s.x) &&
                           Mathf.Abs(s.x - s.z) < 0.02f * Mathf.Abs(s.x);
            var anchor = new GameObject("Trazos sin guardar (" + owner.name + ")").transform;
            // Con escala no uniforme las cintas se deformarían: quedan fijas en el mundo.
            anchor.SetParent(uniform ? owner : transform, false);
            if (!uniform)
                anchor.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            canvas = new InkCanvas(this, null, anchor, null) { OwnsAnchor = true };
            _temporary[owner] = canvas;
            return canvas;
        }

        private InkCanvas HouseCanvas()
        {
            Transform root = Environment() != null ? _environment.HouseRoot : null;
            if (root == null)
                return null;
            if (_houseCanvas != null && _houseRoot == root && _houseCanvas.Anchor != null)
                return _houseCanvas;
            _houseRoot = root;
            string key = "casa/" + (_environment.EnvironmentName ?? "casa");
            _houseCanvas = GetPersistent(key, () =>
            {
                var anchor = new GameObject("Trazos de la casa").transform;
                anchor.SetParent(root, false);
                return anchor;
            }, null, true);
            return _houseCanvas;
        }

        private InkCanvas BookPage(string document, int index)
        {
            Transform parent = _book.leftPage != null ? _book.leftPage.transform.parent : _book.transform;
            return GetPersistent("libro/" + document + "/p" + (index + 1), () =>
            {
                var anchor = new GameObject("Trazos página " + (index + 1)).transform;
                anchor.SetParent(parent, false);
                anchor.gameObject.SetActive(false);
                return anchor;
            }, _book.bookCollider, true);
        }

        private void RefreshBookPages()
        {
            string document = _book != null ? _book.DocumentId : null;
            // Página de cada lado (en manga van cambiadas); -1 = lado vacío.
            int left = _book != null ? _book.LeftSlotPage : -1;
            int rightPage = _book != null ? _book.RightSlotPage : -1;
            if (document == _shownDocument && left == _shownLeft && rightPage == _shownRightPage)
                return;
            _shownDocument = document;
            _shownLeft = left;
            _shownRightPage = rightPage;
            InkCanvas previousLeft = _leftCanvas, previousRight = _rightCanvas;
            _leftCanvas = _rightCanvas = null;
            if (!string.IsNullOrEmpty(document))
            {
                if (left >= 0)
                    _leftCanvas = BookPage(document, left);
                if (rightPage >= 0)
                    _rightCanvas = BookPage(document, rightPage);
            }
            SetShown(previousLeft, false);
            SetShown(previousRight, false);
            SetShown(_leftCanvas, true);
            SetShown(_rightCanvas, true);
            FollowPages();
        }

        private static void SetShown(InkCanvas canvas, bool shown)
        {
            if (canvas != null && canvas.Anchor != null && canvas.Anchor.gameObject.activeSelf != shown)
                canvas.Anchor.gameObject.SetActive(shown);
        }

        private void FollowPages()
        {
            Follow(_leftCanvas, _book != null ? _book.leftPage : null);
            Follow(_rightCanvas, _book != null ? _book.rightPage : null);
        }

        /// <summary>El ancla copia la página con escala uniforme = alto de página: los trazos siguen la escala del libro.</summary>
        private static void Follow(InkCanvas canvas, Renderer page)
        {
            if (canvas == null || canvas.Anchor == null || page == null)
                return;
            Transform p = page.transform;
            canvas.Anchor.localPosition = p.localPosition;
            canvas.Anchor.localRotation = p.localRotation;
            canvas.Anchor.localScale = Vector3.one * Mathf.Max(1e-4f, p.localScale.y);
        }

        // ───────────────────────── Trazos ─────────────────────────

        internal LiveStroke Begin(InkCanvas canvas, int color, bool invisible, Vector3 worldNormal)
        {
            float scale = canvas.Scale;
            var stroke = new InkStroke { Color = Mathf.Clamp(color, 0, InkPalette.Count - 1),
                                         Invisible = invisible,
                                         Width = InkPalette.Width(color, invisible) / scale,
                                         Normal = canvas.Anchor.InverseTransformDirection(worldNormal).normalized,
                                         Live = true };
            canvas.Strokes.Add(stroke);
            if (invisible)
                canvas.InvisibleCount++;
            var live = new LiveStroke { Canvas = canvas, Stroke = stroke };
            if (!invisible)
            {
                live.Mesh = new Mesh { name = "FolioXR trazo en curso" };
                live.Mesh.MarkDynamic();
                live.Object = ToolVisuals.MeshObject(canvas.Anchor, "Trazo en curso", live.Mesh, Materials[stroke.Color])
                                  .gameObject;
            }
            return live;
        }

        /// <summary>Agrega un punto (mundo, ya desplazado sobre la superficie). Solo sube la malla si hubo punto.</summary>
        internal void Append(LiveStroke live, Vector3 worldPoint)
        {
            InkCanvas canvas = live.Canvas;
            if (canvas.Anchor == null || live.Full)
                return;
            Vector3 local = canvas.Anchor.InverseTransformPoint(worldPoint);
            List<Vector3> points = live.Stroke.Points;
            if (points.Count > 0 && (local - points[points.Count - 1]).magnitude * canvas.Scale < MinSpacing)
                return;
            live.Stroke.Add(local);
            if (live.Mesh == null)
                return;
            int count = points.Count;
            if (count <= 2)
            {
                live.Vertices.Clear();
                live.Triangles.Clear();
                InkGeometry.Append(live.Stroke, live.Vertices, live.Triangles);
            }
            else
            {
                // Un par nuevo con la misma tangente hacia atrás que usa InkGeometry: sin saltos al terminar.
                Vector3 side = Vector3.Cross(live.Stroke.Normal, points[count - 1] - points[count - 2]);
                if (side.sqrMagnitude < 1e-14f)
                    side = live.Vertices[live.Vertices.Count - 1] - points[count - 2];
                else
                    side = side.normalized * live.Stroke.Width * 0.5f;
                int k = live.Vertices.Count;
                live.Vertices.Add(points[count - 1] - side);
                live.Vertices.Add(points[count - 1] + side);
                live.Triangles.Add(k - 2);
                live.Triangles.Add(k - 1);
                live.Triangles.Add(k + 1);
                live.Triangles.Add(k - 2);
                live.Triangles.Add(k + 1);
                live.Triangles.Add(k);
            }
            live.Mesh.SetVertices(live.Vertices);
            live.Mesh.SetTriangles(live.Triangles, 0);
            live.Mesh.RecalculateBounds();
        }

        internal void Finish(LiveStroke live)
        {
            if (live == null)
                return;
            if (live.Object != null)
                Destroy(live.Object);
            if (live.Mesh != null)
                Destroy(live.Mesh);
            InkCanvas canvas = live.Canvas;
            InkStroke stroke = live.Stroke;
            if (canvas.Anchor == null || stroke.Points.Count == 0)
            {
                canvas.Strokes.Remove(stroke);
                if (stroke.Invisible)
                    canvas.InvisibleCount = Mathf.Max(0, canvas.InvisibleCount - 1);
                return;
            }
            stroke.RecalculateBounds();
            canvas.AddFinished(stroke);
            if (canvas.Persistent)
                canvas.SaveDue = Time.unscaledTime + SaveDelay;
        }

        /// <summary>Goma: quita los trazos visibles o invisibles que toca.</summary>
        internal int Erase(Vector3 worldPoint, float worldRadius)
        {
            int removed = 0;
            CollectVisible();
            foreach (InkCanvas canvas in _scratch)
            {
                int n = canvas.Erase(worldPoint, worldRadius);
                if (n == 0)
                    continue;
                removed += n;
                if (canvas.Persistent)
                    canvas.SaveDue = Time.unscaledTime + SaveDelay;
            }
            return removed;
        }

        // ───────────────────────── Menú ─────────────────────────

        public static bool HasHouse => Instance != null && Instance.Environment() != null && Instance._environment.HouseRoot != null;

        /// <summary>El controlador de entornos puede crearse después que el registro: se busca de nuevo cada segundo.</summary>
        private FolioEnvironmentController Environment()
        {
            if (_environment == null && Time.unscaledTime >= _nextEnvironmentFind)
            {
                _nextEnvironmentFind = Time.unscaledTime + 1f;
                _environment = FindFirstObjectByType<FolioEnvironmentController>();
            }
            return _environment;
        }

        /// <summary>Borra los trazos de las páginas visibles del libro (ambas de la doble página).</summary>
        public static int ClearVisibleBookPages()
        {
            if (Instance == null)
                return 0;
            Instance.RefreshBookPages();
            int removed = Instance.Clear(Instance._leftCanvas) + Instance.Clear(Instance._rightCanvas);
            Debug.Log("[FolioXR] Trazos: borrados " + removed + " de las páginas visibles");
            return removed;
        }

        public static int ClearHouse()
        {
            if (Instance == null)
                return 0;
            int removed = Instance.Clear(Instance.HouseCanvas());
            Debug.Log("[FolioXR] Trazos: borrados " + removed + " de la casa");
            return removed;
        }

        private int Clear(InkCanvas canvas)
        {
            if (canvas == null)
                return 0;
            int removed = canvas.Strokes.RemoveAll(s => !s.Live);
            canvas.Rebuild();
            if (canvas.Persistent)
                Save(canvas);
            return removed;
        }

        // ───────────────────────── Ciclo ─────────────────────────

        private void LateUpdate()
        {
            if (!_initialized)
                return;
            RefreshBookPages();
            FollowPages();
            float now = Time.unscaledTime;
            if (now >= _nextRebuild)
            {
                _nextRebuild = now + RebuildInterval;
                foreach (InkCanvas canvas in _persistent.Values)
                    if (canvas.RenderDirty && canvas.Anchor != null)
                        canvas.Rebuild();
                foreach (InkCanvas canvas in _temporary.Values)
                    if (canvas.RenderDirty && canvas.Anchor != null)
                        canvas.Rebuild();
            }
            if (now >= _nextReveal)
            {
                _nextReveal = now + RevealInterval;
                UpdateReveal();
            }
            foreach (InkCanvas canvas in _persistent.Values)
                if (canvas.SaveDue > 0f && now >= canvas.SaveDue)
                    Save(canvas);
            if (Time.frameCount % 300 == 0)
                SweepTemporary();
        }

        private void UpdateReveal()
        {
            bool active = UltravioletLight.Active;
            int budget = 64; // Rayos de oclusión por actualización (10 Hz).
            CollectAll();
            foreach (InkCanvas canvas in _scratch)
            {
                if (active && canvas.InvisibleCount > 0 && canvas.Visible)
                    canvas.UpdateReveal(ref budget);
                else
                    canvas.HideReveal();
            }
        }

        private void CollectAll()
        {
            _scratch.Clear();
            foreach (InkCanvas canvas in _persistent.Values)
                if (canvas.Anchor != null)
                    _scratch.Add(canvas);
            foreach (InkCanvas canvas in _temporary.Values)
                if (canvas.Anchor != null)
                    _scratch.Add(canvas);
        }

        private void CollectVisible()
        {
            CollectAll();
            _scratch.RemoveAll(c => !c.Visible);
        }

        private void SweepTemporary()
        {
            var dead = new List<Transform>();
            foreach (KeyValuePair<Transform, InkCanvas> pair in _temporary)
                if (pair.Key == null || pair.Value.Anchor == null)
                    dead.Add(pair.Key);
            foreach (Transform key in dead)
            {
                _temporary[key].Dispose();
                _temporary.Remove(key);
            }
        }

        // ───────────────────────── Guardado ─────────────────────────

        private string PathFor(string key)
        {
            var name = new StringBuilder(key.Length + 16);
            foreach (char c in key)
                name.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '-') ? c : '_');
            if (name.Length > 80)
                name.Length = 80;
            uint hash = 2166136261u; // FNV-1a: estable entre sesiones (string.GetHashCode no lo garantiza).
            foreach (char c in key)
                hash = (hash ^ c) * 16777619u;
            return Path.Combine(_folder, name + "_" + hash.ToString("x8") + ".json");
        }

        private void Load(InkCanvas canvas)
        {
            string path = PathFor(canvas.Key);
            int strokes = 0;
            try
            {
                if (File.Exists(path))
                {
                    InkFile file = JsonUtility.FromJson<InkFile>(File.ReadAllText(path, Encoding.UTF8));
                    if (file != null && file.strokes != null)
                        foreach (InkStrokeData data in file.strokes)
                        {
                            InkStroke stroke = InkGeometry.FromData(data);
                            if (stroke == null)
                                continue;
                            canvas.Strokes.Add(stroke);
                            strokes++;
                        }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Trazos: no se pudo leer " + path + ": " + e.Message);
            }
            canvas.Rebuild();
            if (strokes > 0)
            {
                Totals(out int allStrokes, out int allVertices);
                Debug.Log("[FolioXR] Trazos: cargados " + strokes + " trazos y " + canvas.VertexTotal() + " vértices de «" +
                          canvas.Key + "» (en memoria: " + allStrokes + " trazos, " + allVertices + " vértices)");
            }
        }

        private void Totals(out int strokes, out int vertices)
        {
            strokes = vertices = 0;
            foreach (InkCanvas canvas in _persistent.Values)
            {
                strokes += canvas.Strokes.Count;
                vertices += canvas.VertexTotal();
            }
        }

        private void Save(InkCanvas canvas)
        {
            canvas.SaveDue = -1f;
            if (!canvas.Persistent || _folder == null)
                return;
            var file = new InkFile();
            foreach (InkStroke stroke in canvas.Strokes)
                if (!stroke.Live && stroke.Points.Count > 0)
                    file.strokes.Add(InkGeometry.ToData(stroke));
            string json = JsonUtility.ToJson(file);
            string path = PathFor(canvas.Key);
            lock (_writeLock)
                _writes = _writes.ContinueWith(_ => WriteAtomic(path, json), TaskScheduler.Default);
        }

        /// <summary>Archivo temporal + reemplazo: un corte de energía deja el JSON viejo o el nuevo, nunca uno a medias.</summary>
        private static void WriteAtomic(string path, string json)
        {
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (!File.Exists(path))
                {
                    File.Move(temporary, path);
                    return;
                }
                try
                {
                    File.Replace(temporary, path, null);
                }
                catch (Exception)
                {
                    File.Delete(path);
                    File.Move(temporary, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Trazos: no se pudo guardar " + path + ": " + e.Message);
            }
        }

        private void Flush()
        {
            foreach (InkCanvas canvas in _persistent.Values)
                if (canvas.SaveDue > 0f)
                    Save(canvas);
            Task pending;
            lock (_writeLock)
                pending = _writes;
            try
            {
                pending.Wait(3000);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Trazos: guardado pendiente con error: " + e.Message);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _initialized)
                Flush();
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _initialized)
                Flush();
        }
        private void OnApplicationQuit()
        {
            if (_initialized)
                Flush();
        }

        private void OnDestroy()
        {
            if (_initialized)
                Flush();
            foreach (InkCanvas canvas in _persistent.Values)
                canvas.Dispose();
            foreach (InkCanvas canvas in _temporary.Values)
                canvas.Dispose();
            _persistent.Clear();
            _temporary.Clear();
            if (Instance == this)
                Instance = null;
        }
    }
}
