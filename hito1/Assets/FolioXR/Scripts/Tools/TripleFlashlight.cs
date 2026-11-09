using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FolioXR.Tools
{
    public enum FlashlightMode
    {
        Normal,
        Laser,
        Ultraviolet
    }

    [DefaultExecutionOrder(20020)] // Actualizar rayo después de mover el rig en LateUpdate.
    public sealed class TripleFlashlight : FolioTool
    {
        public FlashlightMode Mode { get; private set; }
        public bool Powered { get; private set; }
        public Transform Emitter => _emitter;
        private Transform _emitter, _powerButton, _modeButton, _cone, _dot;
        private Light _light;
        private LineRenderer _laser;
        private Material _coneMaterial;
        private RenderPipelineAsset _checkedPipeline;
        private bool _pipelineChecked, _pixelLights, _powerArmed, _modeArmed;

        internal void Build(ToolVisuals visuals)
        {
            visuals.Box(transform, "Mango", Vector3.zero, new Vector3(0.03f, 0.035f, 0.12f), visuals.Dark);
            visuals.Box(transform, "Cabezal", new Vector3(0f, 0f, 0.065f), new Vector3(0.045f, 0.045f, 0.03f),
                        visuals.White);
            _powerButton = visuals.Box(transform, "Encender/apagar (verde)", new Vector3(0f, 0.022f, -0.025f),
                                       new Vector3(0.014f, 0.009f, 0.014f), visuals.Cyan);
            _modeButton = visuals.Box(transform, "Modo (violeta)", new Vector3(0f, 0.022f, 0.012f),
                                      new Vector3(0.014f, 0.009f, 0.014f), visuals.Violet);
            _emitter = new GameObject("Emisor +Z").transform;
            _emitter.SetParent(transform, false);
            _emitter.localPosition = new Vector3(0f, 0f, 0.085f); // Fuera del collider propio incluso al soltar.
            _light = _emitter.gameObject.AddComponent<Light>();   // Única luz adicional de toda la tanda.
            _light.type = LightType.Spot;
            _light.shadows = LightShadows.None;
            _light.spotAngle = 35f;
            _light.innerSpotAngle = 20f;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.enabled = false;
            _cone = visuals.SoftCone(_emitter);
            _coneMaterial = visuals.Cone;
            _laser = visuals.Line(_emitter, "Rayo láser", visuals.Red, 0.0012f, 2);
            _dot = visuals.Box(_emitter, "Punto láser", Vector3.zero, Vector3.one * 0.004f, visuals.Red);
            UltravioletLight.Register(this);
            CheckPipeline();
            RefreshMode();
        }

        private void CheckPipeline()
        {
            RenderPipelineAsset active = GraphicsSettings.currentRenderPipeline;
            var urp = active as UniversalRenderPipelineAsset;
            bool pixelLights = urp != null && urp.additionalLightsRenderingMode == LightRenderingMode.PerPixel &&
                               urp.maxAdditionalLightsCount > 0;
            if (_pipelineChecked && _checkedPipeline == active && _pixelLights == pixelLights)
                return;
            _pipelineChecked = true;
            _checkedPipeline = active;
            _pixelLights = pixelLights;
            if (!_pixelLights)
                Debug.LogWarning("[FolioXR] Linterna: URP activo no permite luces adicionales por píxel; se usará " +
                                 "solo el cono falso.");
            RefreshMode();
        }

        public override void ControllerInput(ToolButtons buttons)
        {
            if (buttons.TriggerDown)
                TogglePower();
            if (buttons.PrimaryDown)
                NextMode();
            // B/Y queda reservado al propietario para extensiones; no pasa página ni cierra el menú.
        }

        public override void OtherIndexTouch(Vector3 position, bool reliable)
        {
            if (!reliable)
            {
                _powerArmed = _modeArmed = false;
                return;
            }
            Touch(position, _powerButton, ref _powerArmed, true);
            Touch(position, _modeButton, ref _modeArmed, false);
        }

        private void Touch(Vector3 tip, Transform button, ref bool armed, bool power)
        {
            float distance = Vector3.Distance(tip, button.position);
            if (distance > 0.025f)
                armed = true;
            if (!armed || distance >= 0.012f)
                return;
            armed = false;
            if (power)
                TogglePower();
            else
                NextMode();
        }

        private void TogglePower()
        {
            Powered = !Powered;
            RefreshMode();
            Debug.Log("[FolioXR] Linterna: " + (Powered ? "encendida" : "apagada"));
        }

        private void NextMode()
        {
            Mode = (FlashlightMode)(((int)Mode + 1) % 3);
            RefreshMode();
            Debug.Log("[FolioXR] Linterna: modo " + Mode);
        }

        private void RefreshMode()
        {
            if (_light == null)
                return;
            bool uv = Mode == FlashlightMode.Ultraviolet;
            bool laser = Mode == FlashlightMode.Laser;
            Color color = uv ? new Color(0.55f, 0.12f, 1f) : new Color(1f, 0.95f, 0.8f);
            _light.color = color;
            _light.intensity = uv ? 0.45f : 2f;
            _light.range = uv ? 3f : 20f;
            _light.enabled = Powered && !laser && _pixelLights;
            _coneMaterial.SetColor("_BaseColor", color);
            _cone.gameObject.SetActive(Powered && !laser && (uv || !_pixelLights));
            _cone.localScale = Vector3.one * (uv ? 3f : 20f);
            _laser.enabled = Powered && laser;
            _dot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            CheckPipeline();
            if (!Powered || _emitter == null)
                return;
            bool laser = Mode == FlashlightMode.Laser;
            if (!laser && !_cone.gameObject.activeSelf)
                return;
            float range = Mode == FlashlightMode.Ultraviolet ? 3f : 20f;
            bool hit = Physics.Raycast(_emitter.position, _emitter.forward, out RaycastHit impact, range, ~0,
                                       QueryTriggerInteraction.Ignore);
            float distance = hit ? impact.distance : range;
            if (laser)
            {
                _laser.SetPosition(0, Vector3.zero);
                _laser.SetPosition(1, Vector3.forward * distance);
                _dot.gameObject.SetActive(hit);
                if (hit)
                    _dot.position = impact.point + impact.normal * 0.002f;
            }
            else
                _cone.localScale = Vector3.one * distance; // Evitar prolongar el eje del cono tras el primer sólido.
        }

        protected override void OnTaken()
        {
            _powerArmed = _modeArmed = false;
        }
        protected override void OnReleased()
        {
            _powerArmed = _modeArmed = false;
        }
        protected override void OnReturned()
        {
            Powered = false;
            _powerArmed = _modeArmed = false;
            RefreshMode();
        }
        private void OnDestroy()
        {
            UltravioletLight.Unregister(this);
        }
    }
}
