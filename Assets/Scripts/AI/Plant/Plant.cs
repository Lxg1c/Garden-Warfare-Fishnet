using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace AI.Plant
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    public class Plant : NetworkBehaviour
    {
        public enum State { Neutral, Carried, Placed }

        public readonly SyncVar<State> CurrentState = new SyncVar<State>();
        public readonly SyncVar<int> OwnerActorNumber = new SyncVar<int>();

        [Header("References")]
        [SerializeField] private Rigidbody rb;
        [SerializeField] private Collider col;
        [SerializeField] private PlantTurret turret;

        public bool isActive => CurrentState.Value == State.Placed;

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (col == null) col = GetComponent<Collider>();
            if (turret == null) turret = GetComponent<PlantTurret>();

            CurrentState.OnChange += OnStateChanged;
        }

        private void OnDestroy()
        {
            CurrentState.OnChange -= OnStateChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            OnStateChanged(State.Neutral, CurrentState.Value, false);
        }

        private void OnStateChanged(State prev, State next, bool asServer)
        {
            switch (next)
            {
                case State.Neutral:
                    if (rb) { rb.isKinematic = false; rb.useGravity = true; }
                    if (col) col.enabled = true;
                    if (turret) turret.enabled = false;
                    transform.SetParent(null);
                    break;

                case State.Carried:
                    if (rb) { rb.isKinematic = true; rb.useGravity = false; }
                    if (col) col.enabled = false;
                    if (turret) turret.enabled = false;
                    break;

                case State.Placed:
                    if (rb) { rb.isKinematic = true; rb.useGravity = false; }
                    if (col) col.enabled = true;
                    if (turret) turret.enabled = true;
                    transform.SetParent(null);
                    break;
            }
        }

        // --- SERVER SIDE ---

        public void SetCarried(NetworkObject carrier)
        {
            if (!IsServerInitialized) return;
            CurrentState.Value = State.Carried;
            OwnerActorNumber.Value = -1;
        }

        public void Drop()
        {
            if (!IsServerInitialized) return;
            CurrentState.Value = State.Neutral;
            OwnerActorNumber.Value = -1;
        }

        public void Place(Vector3 pos, float yRot, int ownerId)
        {
            if (!IsServerInitialized) return;

            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yRot, 0);

            OwnerActorNumber.Value = ownerId;
            CurrentState.Value = State.Placed;
        }
    }
}