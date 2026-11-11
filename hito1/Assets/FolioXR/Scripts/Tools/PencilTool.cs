using FolioXR.Tools.Ink;
using UnityEngine;

namespace FolioXR.Tools
{
    /// <summary>
    /// Tanda D (escrita por Claude). Lápiz de 6 colores con goma, o pluma de tinta invisible.
    /// Escribe solo cuando la punta toca una superficie (≤ 5 mm); junto a una regla traza recto.
    /// </summary>
    public sealed class PencilTool : FolioTool
    {
        public bool Invisible { get; private set; }
        public int ColorIndex { get; private set; }

        private const float Length = 0.15f, TipLength = 0.012f, EraserRadius = 0.008f;
        private const float SnapDistance = 0.01f, SnapRelease = 0.015f, MaxBend = 35f, LostGrace = 0.06f;
        private Vector3 _tipLocal, _eraserLocal;
        private Transform _tip, _band;
        private Renderer _tipRenderer, _bandRenderer;
        private Material[] _colorMaterials;
        private LiveStroke _live;
        private float _lostSince = -1f;
        private bool _bandArmed;
        private IStraightEdge _edge;
        private Vector3 _edgeOrigin, _edgeDirection, _edgeOutward;
        private int _erased;

        internal void Build(ToolVisuals visuals, bool invisible)
        {
            Invisible = invisible;
            float half = Length * 0.5f;
            _tipLocal = new Vector3(0f, 0f, half + TipLength);
            _eraserLocal = new Vector3(0f, 0f, -half - 0.01f);
            InkManager ink = InkManager.Instance;
            _colorMaterials = new Material[InkPalette.Count];
            for (int i = 0; i < InkPalette.Count; i++)
                _colorMaterials[i] = ink != null ? ink.Materials[i] : visuals.White;
            if (invisible)
            {
                // Pluma: cuerpo oscuro, capuchón violeta, punta metálica. Sin goma ni colores.
                visuals.Box(transform, "Cuerpo de la pluma", Vector3.zero, new Vector3(0.011f, 0.011f, Length), visuals.Dark);
                visuals.Box(transform, "Anillo violeta", new Vector3(0f, 0f, -half + 0.02f),
                            new Vector3(0.012f, 0.012f, 0.01f), visuals.Violet);
                _tip = visuals.Box(transform, "Plumilla", new Vector3(0f, 0f, half + TipLength * 0.5f),
                                   new Vector3(0.004f, 0.0015f, TipLength), visuals.White);
                return;
            }
            visuals.Box(transform, "Madera", Vector3.zero, new Vector3(0.009f, 0.009f, Length), visuals.White);
            _tip = visuals.Box(transform, "Mina", new Vector3(0f, 0f, half + TipLength * 0.5f),
                               new Vector3(0.004f, 0.004f, TipLength), _colorMaterials[0]);
            _band = visuals.Box(transform, "Extremo de color (tocar para cambiar)", new Vector3(0f, 0f, -half + 0.012f),
                                new Vector3(0.0105f, 0.0105f, 0.016f), _colorMaterials[0]);
            visuals.Box(transform, "Goma", new Vector3(0f, 0f, -half - 0.005f), new Vector3(0.009f, 0.009f, 0.01f),
                        visuals.Red);
            _tipRenderer = _tip.GetComponent<Renderer>();
            _bandRenderer = _band.GetComponent<Renderer>();
        }

        public override void ControllerInput(ToolButtons buttons)
        {
            if (Invisible)
                return;
            if (buttons.PrimaryDown)
                SetColor(ColorIndex + 1);
            else if (buttons.SecondaryDown)
                SetColor(ColorIndex + InkPalette.Count - 1);
        }

        public override void OtherIndexTouch(Vector3 position, bool reliable)
        {
            if (Invisible || _band == null)
                return;
            if (!reliable)
            {
                _bandArmed = false;
                return;
            }
            float distance = Vector3.Distance(position, _band.position);
            if (distance > 0.025f)
                _bandArmed = true;
            else if (_bandArmed && distance < 0.012f)
            {
                _bandArmed = false;
                SetColor(ColorIndex + 1);
            }
        }

        private void SetColor(int index)
        {
            EndStroke();
            ColorIndex = (index % InkPalette.Count + InkPalette.Count) % InkPalette.Count;
            if (_tipRenderer != null)
                _tipRenderer.sharedMaterial = _colorMaterials[ColorIndex];
            if (_bandRenderer != null)
                _bandRenderer.sharedMaterial = _colorMaterials[ColorIndex];
            Debug.Log("[FolioXR] Lápiz: color " + InkPalette.Names[ColorIndex]);
        }

        protected override void OnHeld()
        {
            InkManager ink = InkManager.Instance;
            if (ink == null)
                return;
            Vector3 axis = transform.forward;
            Vector3 tip = transform.TransformPoint(_tipLocal);
            if (ink.Probe(tip, axis, transform, out InkContact contact))
            {
                _lostSince = -1f;
                Draw(ink, contact);
            }
            else if (_live != null)
            {
                // Pequeña tolerancia: el temblor de la mano no corta la línea.
                if (_lostSince < 0f)
                    _lostSince = Time.unscaledTime;
                else if (Time.unscaledTime - _lostSince > LostGrace)
                    EndStroke();
            }
            if (!Invisible)
            {
                Vector3 eraser = transform.TransformPoint(_eraserLocal);
                int removed = ink.Erase(eraser, EraserRadius);
                if (removed > 0)
                {
                    _erased += removed;
                    Debug.Log("[FolioXR] Lápiz: goma borró " + removed + " trazo(s), " + _erased + " en esta toma");
                }
            }
        }

        private void Draw(InkManager ink, InkContact contact)
        {
            Vector3 point = contact.Point;
            Vector3 normal = contact.Normal;
            float halfWidth = InkPalette.Width(ColorIndex, Invisible) * 0.5f;
            bool snapped = false;
            if (_edge != null)
            {
                // Mientras siga a menos de 1,5 cm, la línea queda pegada a la misma recta.
                if (_edge.TryGetEdge(point, SnapRelease, out _edgeOrigin, out _edgeDirection, out _edgeOutward))
                    snapped = true;
                else
                {
                    EndStroke();
                    _edge = null;
                }
            }
            else if (StraightEdges.TryFind(point, SnapDistance, out IStraightEdge edge, out _edgeOrigin, out _edgeDirection,
                                           out _edgeOutward))
            {
                EndStroke(); // Una línea libre no se dobla para hacerse recta: empieza otra.
                _edge = edge;
                snapped = true;
            }
            if (snapped)
            {
                Vector3 onLine = _edgeOrigin + _edgeDirection * Vector3.Dot(point - _edgeOrigin, _edgeDirection);
                onLine += _edgeOutward * (halfWidth + 0.0005f);
                point = onLine - normal * Vector3.Dot(onLine - contact.Point, normal);
            }
            if (_live != null)
            {
                bool sameCanvas = _live.Canvas == contact.Canvas && _live.Canvas.Anchor != null;
                bool bent = sameCanvas &&
                            Vector3.Angle(_live.Canvas.Anchor.TransformDirection(_live.Stroke.Normal), normal) > MaxBend;
                if (!sameCanvas || bent || !_live.Canvas.Visible)
                    EndStroke();
            }
            Vector3 surfacePoint = point + normal * InkPalette.Offset(ColorIndex, Invisible);
            if (_live != null && _live.Full)
            {
                // Trazo largo: se corta y sigue desde el último punto, sin hueco.
                Vector3 last = _live.Canvas.Anchor.TransformPoint(_live.Stroke.Points[_live.Stroke.Points.Count - 1]);
                EndStroke();
                _live = ink.Begin(contact.Canvas, ColorIndex, Invisible, normal);
                ink.Append(_live, last);
            }
            if (_live == null)
                _live = ink.Begin(contact.Canvas, ColorIndex, Invisible, normal);
            ink.Append(_live, surfacePoint);
        }

        private void EndStroke()
        {
            if (_live == null)
                return;
            if (InkManager.Instance != null)
                InkManager.Instance.Finish(_live);
            _live = null;
            _lostSince = -1f;
        }

        protected override void OnTaken()
        {
            _bandArmed = false;
            _erased = 0;
        }
        protected override void OnReleased()
        {
            EndStroke();
            _edge = null;
            _bandArmed = false;
        }
        protected override void OnReturned()
        {
            EndStroke();
            _edge = null;
            _bandArmed = false;
        }
        private void OnDisable()
        {
            EndStroke();
        }
    }
}
