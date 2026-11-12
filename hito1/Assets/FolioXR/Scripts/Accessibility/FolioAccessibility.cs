using System;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Accessibility
{
    /// <summary>
    /// Ajustes de accesibilidad (encargo 18, parte 3; Claude, 12 nov 2026). Se guardan en PlayerPrefs y
    /// avisan con <see cref="Changed"/> para que menús, manos y movimiento se actualicen al momento.
    /// </summary>
    public static class FolioAccessibility
    {
        private const string Prefix = "folioxr.acc.";
        public static event Action Changed;

        /// <summary>Modo zurdo: intercambia las manos de los gestos y de los controles.</summary>
        public static bool LeftHanded => PlayerPrefs.GetInt(Prefix + "zurdo", 0) != 0;
        /// <summary>Alto contraste: menús negro, blanco y amarillo.</summary>
        public static bool HighContrast => PlayerPrefs.GetInt(Prefix + "contraste", 0) != 0;
        /// <summary>Reducir movimiento: sin animación de hoja, sin inercia, giro por pasos y viñeta siempre.</summary>
        public static bool ReduceMotion => PlayerPrefs.GetInt(Prefix + "movimiento", 0) != 0;
        /// <summary>Subtítulos de los avisos: el texto de cada aviso aparece abajo, aunque el menú esté cerrado.</summary>
        public static bool Captions => PlayerPrefs.GetInt(Prefix + "subtitulos", 1) != 0;
        /// <summary>Tamaño base de los menús (1 = normal). El gesto de dos manos lo ajusta después.</summary>
        public static float MenuSize => Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "menu", 1f), 0.5f, 3f);

        public static void SetLeftHanded(bool value) => SetFlag("zurdo", value, "Modo zurdo");
        public static void SetHighContrast(bool value) => SetFlag("contraste", value, "Alto contraste");
        public static void SetReduceMotion(bool value) => SetFlag("movimiento", value, "Reducir movimiento");
        public static void SetCaptions(bool value) => SetFlag("subtitulos", value, "Subtítulos");

        /// <summary>Cambia el tamaño de los dos menús (también el que se ajustó con las manos).</summary>
        public static void SetMenuSize(float size)
        {
            size = Mathf.Clamp(size, 0.5f, 3f);
            PlayerPrefs.SetFloat(Prefix + "menu", size);
            PlayerPrefs.SetFloat("folioxr.menuScale.panel", size);
            PlayerPrefs.SetFloat("folioxr.menuScale.esfera", size);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Accesibilidad · tamaño del menú: " + size.ToString("F1"));
            Changed?.Invoke();
        }

        private static void SetFlag(string key, bool value, string name)
        {
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Accesibilidad · " + name + ": " + (value ? "sí" : "no"));
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Papeles de cada mano. «Apoyo» lleva el reloj, la esfera del menú y el caminar; «principal» toca,
    /// apunta y se teletransporta. Con el modo zurdo se intercambian.
    /// </summary>
    public static class Hands
    {
        public static XRNode Support => FolioAccessibility.LeftHanded ? XRNode.RightHand : XRNode.LeftHand;
        public static XRNode Main => FolioAccessibility.LeftHanded ? XRNode.LeftHand : XRNode.RightHand;
        public static OVRPlugin.Hand SupportHand => FolioAccessibility.LeftHanded ? OVRPlugin.Hand.HandRight : OVRPlugin.Hand.HandLeft;
        public static OVRPlugin.Hand MainHand => FolioAccessibility.LeftHanded ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;

        public static Transform SupportAnchor(OVRCameraRig rig) =>
            rig == null ? null : FolioAccessibility.LeftHanded ? rig.rightControllerAnchor : rig.leftControllerAnchor;

        public static Transform MainAnchor(OVRCameraRig rig) =>
            rig == null ? null : FolioAccessibility.LeftHanded ? rig.leftControllerAnchor : rig.rightControllerAnchor;

        /// <summary>Mano por índice de papel: 0 = apoyo, 1 = principal.</summary>
        public static XRNode Node(int role) => role == 0 ? Support : Main;
    }
}
