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
    public sealed class HouseBuildResult : IDisposable
    {
        public GameObject Root { get; internal set; }
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
