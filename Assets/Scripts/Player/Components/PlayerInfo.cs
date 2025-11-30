using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        public readonly SyncVar<int> ActorNumber = new SyncVar<int>();

        // Локальное свойство
        public Transform SpawnPoint { get; private set; }

        private void Awake()
        {
            ActorNumber.OnChange += OnActorNumberChanged;
        }

        private void OnDestroy()
        {
            ActorNumber.OnChange -= OnActorNumberChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (IsOwner)
            {
                gameObject.name = $"Player_{ActorNumber.Value} (Me)";
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
                ActorNumber.Value = id;
            }
            else
            {
                Debug.LogWarning("[PlayerInfo] Попытка изменить ActorNumber с клиента или до инициализации! Игнорируется.");
            }
        }

        public void SetSpawnPoint(Transform spawnPoint)
        {
            SpawnPoint = spawnPoint;
            Debug.Log($"[PlayerInfo] Spawn point set: {spawnPoint?.name}");
        }

        public int GetActorNumber() => ActorNumber.Value;
    }
}