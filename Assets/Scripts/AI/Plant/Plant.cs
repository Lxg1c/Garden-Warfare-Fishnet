using FishNet.Object;
using FishNet.Object.Synchronizing; // Обязательно для SyncVar<T>
using UnityEngine;

namespace AI.Plant
{
    public class Plant : NetworkBehaviour
    {
        public enum State { Neutral, Carried, Placed }

        // Используем SyncVar<State> вместо атрибута
        public readonly SyncVar<State> CurrentState = new SyncVar<State>();

        private Rigidbody rb;
        private Collider col;

        public bool isActive => CurrentState.Value == State.Placed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();

            // Подписываемся на изменение
            CurrentState.OnChange += OnStateChanged;
        }

        private void OnDestroy()
        {
            CurrentState.OnChange -= OnStateChanged;
        }

        // Вызывается автоматически при изменении .Value
        private void OnStateChanged(State prev, State next, bool asServer)
        {
            switch (next)
            {
                case State.Neutral:
                    if (rb) rb.isKinematic = false;
                    if (col) col.enabled = true;
                    break;

                case State.Carried:
                    if (rb) rb.isKinematic = true;
                    if (col) col.enabled = false;
                    break;

                case State.Placed:
                    if (rb) rb.isKinematic = true;
                    if (col) col.enabled = true;
                    // Активируем турель
                    GetComponent<PlantTurret>()?.Activate();
                    break;
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            // Применяем текущее состояние при входе в сеть
            OnStateChanged(State.Neutral, CurrentState.Value, false);
        }

        // ---------------------------------------------
        // Методы управления (ТОЛЬКО СЕРВЕР)
        // ---------------------------------------------

        public void SetCarried(NetworkObject carrier)
        {
            // ИСПРАВЛЕНИЕ: IsServer -> IsServerInitialized
            if (!IsServerInitialized) return;

            CurrentState.Value = State.Carried;
        }

        public void Drop()
        {
            // ИСПРАВЛЕНИЕ: IsServer -> IsServerInitialized
            if (!IsServerInitialized) return;

            CurrentState.Value = State.Neutral;
        }

        public void Place(Vector3 pos, float yRot)
        {
            // ИСПРАВЛЕНИЕ: IsServer -> IsServerInitialized
            if (!IsServerInitialized) return;

            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yRot, 0);

            CurrentState.Value = State.Placed;
        }
    }
}