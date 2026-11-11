using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Tools.Ink
{
    /// <summary>
    /// Un lienzo por página del libro, página de la libreta, casa o superficie temporal.
    /// Los trazos terminados se combinan en trozos de malla por color (≤ 16.000 vértices);
    /// la tinta invisible no se combina: se dibuja solo lo que ilumina la luz UV.
    /// </summary>
    internal sealed class InkCanvas
    {
        private const int ChunkVertices = 16000;

        private sealed class Chunk
        {
            internal GameObject Object;
            internal Mesh Mesh;
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<int> Triangles = new List<int>();
        }

        internal readonly string Key; // null = temporal (no se guarda).
        internal readonly Transform Anchor;
        internal readonly Collider Support;
        internal readonly List<InkStroke> Strokes = new List<InkStroke>();
        internal float SaveDue = -1f;
        internal bool RenderDirty;
        internal bool OwnsAnchor;
        internal int InvisibleCount;
        internal bool RevealShown;

        private readonly InkManager _owner;
        private readonly List<Chunk>[] _chunks = new List<Chunk>[InkPalette.Count];
        private Mesh _revealMesh;
        private GameObject _revealObject;
        private static readonly List<Vector3> RevealVertices = new List<Vector3>();
        private static readonly List<int> RevealTriangles = new List<int>();
        private static readonly List<Vector3> RunPoints = new List<Vector3>(InkGeometry.MaxPoints);

        internal bool Persistent => Key != null;
        internal bool Visible => Anchor != null && Anchor.gameObject.activeInHierarchy;
        internal float Scale => Anchor != null ? Mathf.Max(1e-4f, Anchor.lossyScale.x) : 1f;

        internal InkCanvas(InkManager owner, string key, Transform anchor, Collider support)
        {
            _owner = owner;
            Key = key;
            Anchor = anchor;
            Support = support;
            for (int i = 0; i < _chunks.Length; i++)
                _chunks[i] = new List<Chunk>();
        }

        internal void AddFinished(InkStroke stroke)
        {
            stroke.Live = false;
            if (stroke.Invisible)
                return;
            List<Chunk> chunks = _chunks[stroke.Color];
            Chunk chunk = chunks.Count > 0 ? chunks[chunks.Count - 1] : null;
            if (chunk == null || chunk.Vertices.Count + InkGeometry.VertexCount(stroke) > ChunkVertices)
                chunks.Add(chunk = NewChunk(stroke.Color, chunks.Count));
            InkGeometry.Append(stroke, chunk.Vertices, chunk.Triangles);
            Apply(chunk);
        }

        /// <summary>Rehace todos los trozos (tras cargar o borrar).</summary>
        internal void Rebuild()
        {
            RenderDirty = false;
            InvisibleCount = 0;
            for (int color = 0; color < _chunks.Length; color++)
            {
                List<Chunk> chunks = _chunks[color];
                foreach (Chunk chunk in chunks)
                {
                    chunk.Vertices.Clear();
                    chunk.Triangles.Clear();
                }
                int used = 0;
                foreach (InkStroke stroke in Strokes)
                {
                    if (stroke.Invisible || stroke.Live || stroke.Color != color)
                        continue;
                    if (used == 0 || chunks[used - 1].Vertices.Count + InkGeometry.VertexCount(stroke) > ChunkVertices)
                    {
                        if (used == chunks.Count)
                            chunks.Add(NewChunk(color, used));
                        used++;
                    }
                    InkGeometry.Append(stroke, chunks[used - 1].Vertices, chunks[used - 1].Triangles);
                }
                for (int i = 0; i < chunks.Count; i++)
                    if (i < used)
                        Apply(chunks[i]);
                for (int i = chunks.Count - 1; i >= used; i--)
                {
                    DestroyChunk(chunks[i]);
                    chunks.RemoveAt(i);
                }
            }
            foreach (InkStroke stroke in Strokes)
                if (stroke.Invisible)
                    InvisibleCount++;
            if (InvisibleCount == 0)
                HideReveal();
        }

        internal int VertexTotal()
        {
            int total = 0;
            foreach (List<Chunk> chunks in _chunks)
                foreach (Chunk chunk in chunks)
                    total += chunk.Vertices.Count;
            return total;
        }

        /// <summary>Quita los trazos que toca la goma (punto y radio en metros). Devuelve cuántos quitó.</summary>
        internal int Erase(Vector3 worldPoint, float worldRadius)
        {
            if (Strokes.Count == 0 || !Visible)
                return 0;
            Vector3 local = Anchor.InverseTransformPoint(worldPoint);
            float radius = worldRadius / Scale;
            float radiusSq = radius * radius;
            int removed = 0;
            for (int i = Strokes.Count - 1; i >= 0; i--)
            {
                InkStroke stroke = Strokes[i];
                if (stroke.Live)
                    continue;
                Bounds bounds = stroke.Bounds;
                bounds.Expand(radius * 2f);
                if (!bounds.Contains(local))
                    continue;
                for (int p = 0; p < stroke.Points.Count; p++)
                {
                    Vector3 a = stroke.Points[p];
                    Vector3 b = p + 1 < stroke.Points.Count ? stroke.Points[p + 1] : a;
                    if (SegmentDistanceSq(local, a, b) > radiusSq)
                        continue;
                    Strokes.RemoveAt(i);
                    removed++;
                    break;
                }
            }
            if (removed > 0)
                RenderDirty = true;
            return removed;
        }

        private static float SegmentDistanceSq(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }

        /// <summary>Dibuja solo los tramos de tinta invisible dentro del cono UV (≤ 10 Hz).</summary>
        internal void UpdateReveal(ref int raycastBudget)
        {
            RevealVertices.Clear();
            RevealTriangles.Clear();
            if (InvisibleCount > 0 && Visible && UltravioletLight.Active)
            {
                Vector3 emitter = UltravioletLight.EmitterPosition;
                float scale = Scale;
                foreach (InkStroke stroke in Strokes)
                {
                    if (!stroke.Invisible || stroke.Points.Count == 0)
                        continue;
                    Vector3 center = Anchor.TransformPoint(stroke.Bounds.center);
                    float reach = stroke.Bounds.extents.magnitude * scale;
                    if ((center - emitter).sqrMagnitude > (3f + reach) * (3f + reach))
                        continue;
                    // Un rayo de oclusión por trazo; el resto es geometría del cono.
                    if (raycastBudget <= 0)
                        break;
                    raycastBudget--;
                    Vector3 probe = Anchor.TransformPoint(stroke.Points[stroke.Points.Count / 2]);
                    if (!UltravioletLight.Reveals(probe, Support) && !UltravioletLight.Reveals(center, Support))
                        continue;
                    AppendLitRuns(stroke);
                }
            }
            if (RevealVertices.Count == 0)
            {
                HideReveal();
                return;
            }
            if (_revealMesh == null)
            {
                _revealMesh = new Mesh { name = "FolioXR tinta revelada" };
                _revealMesh.MarkDynamic();
                _revealObject = ToolVisuals.MeshObject(Anchor, "Tinta invisible revelada", _revealMesh,
                                                       _owner.RevealMaterial).gameObject;
            }
            _revealMesh.Clear();
            _revealMesh.indexFormat = RevealVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _revealMesh.SetVertices(RevealVertices);
            _revealMesh.SetTriangles(RevealTriangles, 0);
            _revealMesh.RecalculateBounds();
            _revealObject.SetActive(true);
            RevealShown = true;
        }

        private void AppendLitRuns(InkStroke stroke)
        {
            RunPoints.Clear();
            for (int i = 0; i <= stroke.Points.Count; i++)
            {
                bool lit = i < stroke.Points.Count && UltravioletLight.InCone(Anchor.TransformPoint(stroke.Points[i]));
                if (lit)
                {
                    RunPoints.Add(stroke.Points[i]);
                    continue;
                }
                if (RunPoints.Count > 0)
                    InkGeometry.Append(RunPoints, 0, RunPoints.Count, stroke.Normal, stroke.Width, RevealVertices,
                                       RevealTriangles);
                RunPoints.Clear();
            }
        }

        internal void HideReveal()
        {
            if (!RevealShown)
                return;
            RevealShown = false;
            if (_revealObject != null)
                _revealObject.SetActive(false);
        }

        private Chunk NewChunk(int color, int index)
        {
            var chunk = new Chunk { Mesh = new Mesh { name = "FolioXR trazos " + InkPalette.Names[color] } };
            chunk.Mesh.MarkDynamic();
            chunk.Object = ToolVisuals.MeshObject(Anchor, "Trazos " + InkPalette.Names[color] + " " + index, chunk.Mesh,
                                                  _owner.Materials[color]).gameObject;
            return chunk;
        }

        private static void Apply(Chunk chunk)
        {
            chunk.Mesh.Clear();
            chunk.Mesh.SetVertices(chunk.Vertices);
            chunk.Mesh.SetTriangles(chunk.Triangles, 0);
            chunk.Mesh.RecalculateBounds();
        }

        private static void DestroyChunk(Chunk chunk)
        {
            if (chunk.Object != null)
                Object.Destroy(chunk.Object);
            if (chunk.Mesh != null)
                Object.Destroy(chunk.Mesh);
        }

        internal void Dispose()
        {
            foreach (List<Chunk> chunks in _chunks)
            {
                foreach (Chunk chunk in chunks)
                    DestroyChunk(chunk);
                chunks.Clear();
            }
            if (_revealObject != null)
                Object.Destroy(_revealObject);
            if (_revealMesh != null)
                Object.Destroy(_revealMesh);
            if (OwnsAnchor && Anchor != null)
                Object.Destroy(Anchor.gameObject);
        }
    }
}
