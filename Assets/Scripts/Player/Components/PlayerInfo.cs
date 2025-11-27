using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        // Используем SyncVar<int> вместо атрибута [SyncVar]
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
            // ИСПРАВЛЕНИЕ: Используем IsServerInitialized вместо IsServer
            if (IsServerInitialized)
            {
                ActorNumber.Value = id;
            }
            else
            {
                // Если мы еще не на сервере или это клиент, логируем предупреждение
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