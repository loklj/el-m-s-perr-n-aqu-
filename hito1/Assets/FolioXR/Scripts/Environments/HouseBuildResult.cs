using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Environments
{
    public sealed class ReadingPoint
    {
        public string Name { get; }
        public Vector3 EyePosition { get; }
        public Vector3 ViewDirection { get; }
        public Vector3 FloorPosition { get; }

        public ReadingPoint(string name, Vector3 eyePosition, Vector3 viewDirection, Vector3 floorPosition)
        {
            Name = name;
            EyePosition = eyePosition;
            FloorPosition = floorPosition;
            ViewDirection = viewDirection.normalized;
        }
    }

    /// <summary>Raíz y puntos locales. Posee exclusivamente los recursos creados por Build.</summary>
    /// <summary>
    /// Una fila de la estantería de la biblioteca donde caben los libros del usuario (encargo 18). Coordenadas
    /// locales de la raíz de la casa: Left y Right sobre la tabla, en el borde delantero de los libros pintados.
    /// </summary>
    public sealed class ShelfRow
    {
        public Vector3 Left { get; }
        public Vector3 Right { get; }
        public Vector3 Facing { get; }
        public ShelfRow(Vector3 left, Vector3 right, Vector3 facing) { Left = left; Right = right; Facing = facing; }
    }

    public sealed class HouseBuildResult : IDisposable
    {
        public GameObject Root { get; internal set; }
        /// <summary>Filas libres para la estantería 3D, de la más cómoda a la menos cómoda.</summary>
        public IReadOnlyList<ShelfRow> ShelfRows { get; internal set; } = Array.Empty<ShelfRow>();
        public IReadOnlyList<ReadingPoint> ReadingPoints { get; internal set; }
        public int TriangleCount { get; internal set; }
        public int MeshCount { get; internal set; }
        public int MaterialCount { get; internal set; }

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private bool _disposed;
        internal readonly HashSet<Collider> TeleportFloors = new HashSet<Collider>();

        internal T Own<T>(T resource) where T : UnityEngine.Object
        {
            _owned.Add(resource);
            return resource;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Root != null)
            {
                Root.SetActive(false);
                UnityEngine.Object.Destroy(Root);
            }
            foreach (UnityEngine.Object resource in _owned)
                if (resource != null) UnityEngine.Object.Destroy(resource);
            _owned.Clear();
            TeleportFloors.Clear();
            Root = null;
        }
    }
}
