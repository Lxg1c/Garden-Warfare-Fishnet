using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        private readonly SyncVar<int> _actorNumber = new SyncVar<int>();

        // Синхронизируем позицию спавна (SyncVar чтобы работало и на сервере)
        private readonly SyncVar<Vector3> _syncSpawnPosition = new SyncVar<Vector3>();
        private readonly SyncVar<Quaternion> _syncSpawnRotation = new SyncVar<Quaternion>();

        public Transform SpawnPoint { get; private set; }
        public Vector3 SpawnPosition => _syncSpawnPosition.Value;
        public Quaternion SpawnRotation => _syncSpawnRotation.Value;

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
        }

        // Временные переменные для хранения до сетевой инициализации
        private Vector3 _pendingSpawnPosition;
        private Quaternion _pendingSpawnRotation;
        private bool _hasPendingSpawn;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            // Если была отложенная позиция спавна - применяем её
            if (_hasPendingSpawn && IsServerInitialized)
            {
                _syncSpawnPosition.Value = _pendingSpawnPosition;
                _syncSpawnRotation.Value = _pendingSpawnRotation;
                _hasPendingSpawn = false;
            }
        }

        /// <summary>
        /// Устанавливает точку спавна для игрока (вызывается при первом спавне)
        /// Может быть вызван до или после сетевой инициализации
        /// </summary>
        public void SetSpawnPoint(Transform spawnPoint)
        {
            SpawnPoint = spawnPoint;
            if (spawnPoint == null) return;

            Vector3 pos = spawnPoint.position;
            Quaternion rot = spawnPoint.rotation;

            if (IsServerInitialized)
            {
                _syncSpawnPosition.Value = pos;
                _syncSpawnRotation.Value = rot;
            }
            else
            {
                // Сохраняем для отложенной инициализации
                _pendingSpawnPosition = pos;
                _pendingSpawnRotation = rot;
                _hasPendingSpawn = true;
            }
        }

        /// <summary>
        /// Устанавливает точку спавна по позиции (если Transform недоступен)
        /// </summary>
        public void SetSpawnPosition(Vector3 position, Quaternion rotation)
        {
            if (IsServerInitialized)
            {
                _syncSpawnPosition.Value = position;
                _syncSpawnRotation.Value = rotation;
            }
            else
            {
                _pendingSpawnPosition = position;
                _pendingSpawnRotation = rotation;
                _hasPendingSpawn = true;
            }
        }
    }
}