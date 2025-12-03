using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        private readonly SyncVar<int> _actorNumber = new SyncVar<int>();
        
        public Transform SpawnPoint { get; private set; }

        private void Awake()
        {
            _actorNumber.OnChange += OnActorNumberChanged;
        }

        private void OnDestroy()    
        {
            _actorNumber.OnChange -= OnActorNumberChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (IsOwner)
            {
                gameObject.name = $"Player_{_actorNumber.Value} (Me)";
            }
        }

        private void OnActorNumberChanged(int oldVal, int newVal, bool asServer)
        {
            Debug.Log($"[PlayerInfo] ID изменен: {oldVal} -> {newVal}");
            gameObject.name = $"Player_{newVal} {(IsOwner ? "(Me)" : "")}";
        }

        // ------------------------------------------
        // Публичные методы
        // ------------------------------------------

        public void SetActorNumber(int id)
        {
            if (IsServerInitialized)
            {
                _actorNumber.Value = id;
            }
            else
            {
                Debug.LogWarning("[PlayerInfo] Попытка изменить ActorNumber с клиента или до инициализации! Игнорируется.");
            }
        }
    }
}