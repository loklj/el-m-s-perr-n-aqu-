using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Tools
{
    public enum ToolState
    {
        Stored,
        Held,
        World
    }
    public enum ToolKind
    {
        Magnifier,
        Notebook,
        Pencils,
        InvisibleInk,
        Flashlight,
        Ruler
    }

    /// <summary>Entrada del mando propietario; los flancos se rearman al soltar cada botón.</summary>
    public struct ToolButtons
    {
        public bool Trigger, Primary, Secondary;
        public bool TriggerDown, PrimaryDown, SecondaryDown;
        public Vector2 Stick;
    }

    /// <summary>Contrato de las tandas C–E. El registro arbitra la toma y llama estos callbacks.</summary>
    public abstract class FolioTool : MonoBehaviour
    {
        public ToolKind Kind { get; private set; }
        public ToolState State { get; private set; } = ToolState.Stored;
        public XRNode HolderSide { get; private set; }
        public bool HeldByHand { get; private set; }
        public virtual bool Available => true;
        internal Rigidbody Body { get; private set; }
        internal BoxCollider GripCollider { get; private set; }
        protected ToolRegistry Registry { get; private set; }

        internal void Initialize(ToolRegistry registry, ToolKind kind, Vector3 size)
        {
            Registry = registry;
            Kind = kind;
            GripCollider = gameObject.AddComponent<BoxCollider>();
            GripCollider.size = size;
            Body = gameObject.AddComponent<Rigidbody>();
            Body.mass = 0.15f;
            Body.linearDamping = 0.15f;
            Body.angularDamping = 0.25f;
            Freeze();
            GripCollider.enabled = false;
            gameObject.SetActive(false);
        }

        private void Freeze()
        {
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.isKinematic = true;
            Body.useGravity = false;
            Body.interpolation = RigidbodyInterpolation.None;
        }

        internal void Take(XRNode side, bool hand, Transform trackingSpace, Vector3 position, Quaternion rotation)
        {
            Freeze();
            HolderSide = side;
            HeldByHand = hand;
            State = ToolState.Held;
            // El padre acompaña también las recolocaciones del entorno y la locomoción tardía.
            transform.SetParent(trackingSpace, true);
            transform.SetPositionAndRotation(position, rotation);
            GripCollider.enabled = false; // No empuja la cápsula ni el libro al sostenerse.
            gameObject.SetActive(true);
            OnTaken();
            Debug.Log("[FolioXR] Herramienta: tomar " + Kind + " con " + side + (hand ? " (mano)" : " (mando)"));
        }

        internal void Hold(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            OnHeld();
        }

        internal void Release(bool realistic)
        {
            transform.SetParent(Registry.transform, true);
            State = ToolState.World;
            GripCollider.enabled = true;
            // Caída desde reposo: no se incorpora la velocidad artificial de un salto del rig.
            if (realistic)
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                Body.interpolation = RigidbodyInterpolation.Interpolate;
                Body.WakeUp();
            }
            OnReleased();
            Debug.Log("[FolioXR] Herramienta: soltar " + Kind + (realistic ? " (Realista)" : " (Flotante)"));
        }

        internal void ReturnToWatch()
        {
            Freeze();
            State = ToolState.Stored;
            GripCollider.enabled = false;
            transform.SetParent(Registry.transform, false);
            OnReturned();
            gameObject.SetActive(false);
            Debug.Log("[FolioXR] Herramienta: devolver " + Kind);
        }

        internal void FreezeWorld()
        {
            if (State == ToolState.World)
                Freeze();
        }
        protected virtual void OnTaken()
        {
        }
        protected virtual void OnHeld()
        {
        }
        protected virtual void OnReleased()
        {
        }
        protected virtual void OnReturned()
        {
        }
        public virtual void ControllerInput(ToolButtons buttons)
        {
        }
        /// <summary>Índice de la mano contraria; reliable=false desarma cualquier toque pendiente.</summary>
        public virtual void OtherIndexTouch(Vector3 position, bool reliable)
        {
        }
    }
}
