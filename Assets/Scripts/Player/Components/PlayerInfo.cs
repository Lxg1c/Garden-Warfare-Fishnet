using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        private readonly SyncVar<int> _actorNumber = new SyncVar<int>();

        // Сохраняем позицию спавна (Transform может быть уничтожен, поэтому храним данные)
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        public Transform SpawnPoint { get; private set; }
        public Vector3 SpawnPosition => _spawnPosition;
        public Quaternion SpawnRotation => _spawnRotation;

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

        /// <summary>
        /// Устанавливает точку спавна для игрока (вызывается при первом спавне)
        /// </summary>
        public void SetSpawnPoint(Transform spawnPoint)
        {
            SpawnPoint = spawnPoint;
            if (spawnPoint != null)
            {
                _spawnPosition = spawnPoint.position;
                _spawnRotation = spawnPoint.rotation;
            }
            Debug.Log($"[PlayerInfo] SpawnPoint set to {_spawnPosition}");
        }

        /// <summary>
        /// Устанавливает точку спавна по позиции (если Transform недоступен)
        /// </summary>
        public void SetSpawnPosition(Vector3 position, Quaternion rotation)
        {
            _spawnPosition = position;
            _spawnRotation = rotation;
            Debug.Log($"[PlayerInfo] SpawnPosition set to {_spawnPosition}");
        }
    }
}