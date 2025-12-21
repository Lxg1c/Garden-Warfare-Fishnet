using Core.Components;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Core.Settings;
using FischlWorks_FogWar;
using Gameplay.TurretPlant;
using Player;
using AI.Wave;

namespace Gameplay
{
    public enum LifeFruitState
    {
        Planted,    // Посажен на базе
        Carried,    // Несут
        Dropped     // Брошен (можно подобрать)
    }

    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(csFogVisibilityAgent))]
    public class LifeFruit : NetworkBehaviour
    {
        [Header("Visual")]
        [SerializeField] private GameObject fruitModel; // 3D модель плода (уничтожается при смерти)
        [SerializeField] private GameObject plantingZoneVisual; // Зона посадки (остаётся)

        [Header("Pickup Settings")]
        [SerializeField] private float pickupTime = 2f; // Время для подбора чужого LifeFruit

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<LifeFruitState> _state = new(LifeFruitState.Planted);
        private readonly SyncVar<int> _carrierId = new(-1);

        // ==================
        // Локальные данные
        // ==================

        private Health _health;
        private csFogVisibilityAgent _visibilityAgent;
        private Collider _collider;

        // Для отмены урона от владельца
        private float _healthBeforeDamage;

        // Оригинальная позиция LifeFruit (для возврата после кражи)
        private readonly SyncVar<Vector3> _originalPosition = new();
        private readonly SyncVar<Quaternion> _originalRotation = new();

        // ==================
        // Properties
        // ==================

        public LifeFruitState State => _state.Value;
        public int CarrierId => _carrierId.Value;

        /// <summary>
        /// Можно подобрать если брошен или чужой посаженный (не свой!)
        /// ВАЖНО: Нельзя красть если у игрока уже есть свой живой LifeFruit
        /// ВАЖНО: Нельзя красть если у владельца есть живые турели
        /// ВАЖНО: Нельзя красть во время активной волны
        /// </summary>
        public bool CanBePickedUp(int playerId)
        {
            // Свой LifeFruit нельзя подбирать
            if (OwnerId == playerId) return false;

            // Нельзя подобрать мёртвый LifeFruit
            if (IsDead) return false;

            // Нельзя нести (уже кто-то несёт)
            if (_state.Value == LifeFruitState.Carried) return false;

            // Нельзя красть во время активной волны
            if (WaveManager.Instance != null && WaveManager.Instance.WaveActive)
            {
                Debug.Log($"[LifeFruit] Cannot steal - wave is active");
                return false;
            }

            // Проверяем есть ли у игрока свой ЖИВОЙ LifeFruit (если есть - нельзя красть)
            var playerLifeFruit = TurretPlantManager.Instance?.FindLifeFruitForPlayer(playerId);
            if (playerLifeFruit != null && playerLifeFruit.IsAlive)
            {
                // У игрока уже есть живой посаженный LifeFruit - красть нельзя
                Debug.Log($"[LifeFruit] Player {playerId} cannot steal - has alive LifeFruit");
                return false;
            }

            // Проверяем есть ли у владельца этого LifeFruit живые турели
            int ownerTurretCount = TurretPlantManager.Instance?.GetTurretCount(OwnerId) ?? 0;
            if (ownerTurretCount > 0)
            {
                // Сначала нужно уничтожить все турели владельца
                Debug.Log($"[LifeFruit] Cannot steal - owner {OwnerId} has {ownerTurretCount} turrets");
                return false;
            }

            // Можно подобрать если брошен
            if (_state.Value == LifeFruitState.Dropped) return true;

            // Можно подобрать чужой посаженный (только если у игрока нет своего живого)
            if (_state.Value == LifeFruitState.Planted) return true;

            return false;
        }

        /// <summary>
        /// Проверяет жив ли этот LifeFruit
        /// </summary>
        public bool IsDead => _health != null && _health.IsDead;

        /// <summary>
        /// Проверяет жив ли и посажен ли этот LifeFruit
        /// </summary>
        public bool IsAlive => !IsDead && _state.Value == LifeFruitState.Planted;

        /// <summary>
        /// Оригинальная позиция LifeFruit (куда нужно вернуть после кражи)
        /// </summary>
        public Vector3 OriginalPosition => _originalPosition.Value;
        public Quaternion OriginalRotation => _originalRotation.Value;

        /// <summary>
        /// Время подбора (мгновенно если брошен, долго если посажен)
        /// </summary>
        public float GetPickupTime(int playerId)
        {
            if (_state.Value == LifeFruitState.Dropped) return 0.5f;
            return pickupTime;
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            _visibilityAgent = GetComponent<csFogVisibilityAgent>();
            _collider = GetComponent<Collider>();

            _health.OnDamaged += OnDamaged;
            _health.OnDeath += OnDeath;

            // Скрываем зону посадки по умолчанию
            if (plantingZoneVisual != null)
            {
                plantingZoneVisual.SetActive(false);
            }
        }

        // ==================
        // PLANTING ZONE (вызывается локально через SendMessage)
        // ==================

        /// <summary>
        /// Показывает зону посадки (вызывается только на клиенте владельца)
        /// </summary>
        public void ShowPlantingZone()
        {
            if (plantingZoneVisual != null)
            {
                plantingZoneVisual.SetActive(true);
            }
        }

        /// <summary>
        /// Скрывает зону посадки
        /// </summary>
        public void HidePlantingZone()
        {
            if (plantingZoneVisual != null)
            {
                plantingZoneVisual.SetActive(false);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Сохраняем оригинальную позицию
            _originalPosition.Value = transform.position;
            _originalRotation.Value = transform.rotation;

            // Инициализируем здоровье для отмены урона
            _healthBeforeDamage = _health.GetHealth();

            Debug.Log($"[Server] LifeFruit spawned for player {OwnerId} at {transform.position}");

            RespawnManager.SetRespawnEnabled(OwnerId, true);
        }
        
        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[Client] LifeFruit({OwnerId}) created; trying to attach to fog...");
            
            TryAttachToPlayerFog();
            
            PlayerInitializer.OnPlayerFogReady += ApplyFog;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            
            PlayerInitializer.OnPlayerFogReady -= ApplyFog;
        }

        private void TryAttachToPlayerFog()
        {
            var conn = NetworkManager?.ClientManager?.Connection;
            if (conn == null) return;

            var playerObj = conn.FirstObject;
            if (playerObj == null) return;

            var initializer = playerObj.GetComponent<PlayerInitializer>();
            if (initializer == null) return;

            var fog = initializer.fogController?.GetFogInstance();
            if (fog != null)
            {
                ApplyFog(initializer.OwnerId, fog);
            }
        }
        
        private void ApplyFog(int playerId, csFogWar fog)
        {
            if (playerId != OwnerId) return;

            if (_visibilityAgent == null)
                _visibilityAgent = GetComponent<csFogVisibilityAgent>();

            if (_visibilityAgent != null)
            {
                _visibilityAgent.SetFogWar(fog);
                
                fog._FogRevealers.Add(
                    new csFogWar.FogRevealer(transform, 4, true)
                );
            }
        }

        // ==================
        // PICKUP/DROP/PLANT
        // ==================

        /// <summary>
        /// Подбирает LifeFruit (вызывается сервером)
        /// </summary>
        [Server]
        public bool TryPickup(int playerId)
        {
            if (!CanBePickedUp(playerId)) return false;

            _state.Value = LifeFruitState.Carried;
            _carrierId.Value = playerId;

            // Отключаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            // Отключаем респавн для бывшего владельца
            RespawnManager.SetRespawnEnabled(OwnerId, false);

            Debug.Log($"[Server] LifeFruit({OwnerId}) picked up by player {playerId}");
            return true;
        }

        /// <summary>
        /// Привязывает LifeFruit к игроку
        /// </summary>
        [Server]
        public void AttachToPlayer(Transform playerTransform, Vector3 localOffset)
        {
            if (_state.Value != LifeFruitState.Carried) return;

            transform.SetParent(playerTransform);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.identity;

            AttachToPlayerObserversRpc(playerTransform.GetComponent<NetworkObject>(), localOffset);
        }

        [ObserversRpc]
        private void AttachToPlayerObserversRpc(NetworkObject playerNetObj, Vector3 localOffset)
        {
            if (playerNetObj == null) return;

            transform.SetParent(playerNetObj.transform);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Бросает LifeFruit
        /// </summary>
        [Server]
        public void Drop(Vector3 dropPosition)
        {
            if (_state.Value != LifeFruitState.Carried) return;

            transform.SetParent(null);
            transform.position = dropPosition;
            _state.Value = LifeFruitState.Dropped;
            _carrierId.Value = -1;

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            DetachObserversRpc();

            Debug.Log($"[Server] LifeFruit({OwnerId}) dropped at {dropPosition}");
        }

        [ObserversRpc]
        private void DetachObserversRpc()
        {
            transform.SetParent(null);
        }

        /// <summary>
        /// Сажает LifeFruit на базе нового владельца
        /// </summary>
        [Server]
        public void PlantForNewOwner(int newOwnerId, Vector3 position)
        {
            if (_state.Value != LifeFruitState.Carried) return;

            transform.SetParent(null);
            transform.position = position;
            _state.Value = LifeFruitState.Planted;
            _carrierId.Value = -1;

            // Меняем владельца
            int oldOwnerId = OwnerId;
            GiveOwnership(NetworkManager.ServerManager.Clients[newOwnerId]);

            // Включаем коллайдер
            if (_collider != null)
            {
                _collider.enabled = true;
            }

            // Включаем респавн для нового владельца
            RespawnManager.SetRespawnEnabled(newOwnerId, true);

            // Восстанавливаем здоровье
            if (_health != null)
            {
                _health.SetHealth(_health.GetMaxHealth());
            }

            DetachObserversRpc();

            Debug.Log($"[Server] LifeFruit planted for new owner {newOwnerId} (was {oldOwnerId}) at {position}");
        }

        // ==================
        // DAMAGE HANDLING
        // ==================

        private void LateUpdate()
        {
            // Сохраняем текущее здоровье в конце кадра для возможности отмены урона в следующем
            if (_health != null && !_health.IsDead)
            {
                _healthBeforeDamage = _health.GetHealth();
            }
        }

        private void OnDamaged(Transform attacker)
        {
            if (!IsServerInitialized) return;

            // Вычисляем нанесённый урон
            float currentHealth = _health.GetHealth();
            float damageDealt = _healthBeforeDamage - currentHealth;

            if (attacker != null)
            {
                var attackerNo = attacker.GetComponent<NetworkObject>();

                if (attackerNo != null && attackerNo.OwnerId == OwnerId)
                {
                    // Отменяем урон - восстанавливаем здоровье до значения перед уроном
                    Debug.Log($"[Server] LifeFruit({OwnerId}) ignored self damage ({damageDealt}), restoring to {_healthBeforeDamage}");
                    _health.SetHealth(_healthBeforeDamage);
                    return;
                }
            }

            Debug.Log($"[Server] LifeFruit({OwnerId}) took {damageDealt} damage from {attacker?.name}, health: {currentHealth}");
        }

        private void OnDeath()
        {
            if (!IsServerInitialized) return;

            Debug.Log($"[Server] LifeFruit({OwnerId}) destroyed");

            RespawnManager.SetRespawnEnabled(OwnerId, false);

            NotifyPlayerInitializer();

            // Скрываем только модель плода, зона остаётся видимой
            HideFruitModelObserversRpc();

            // НЕ деспавним объект - зона должна остаться для понимания где можно посадить новый LifeFruit
            // Отключаем коллайдер чтобы нельзя было взаимодействовать
            if (_collider != null)
            {
                _collider.enabled = false;
            }
        }

        [ObserversRpc]
        private void HideFruitModelObserversRpc()
        {
            if (fruitModel != null)
            {
                fruitModel.SetActive(false);
            }
        }

        [Server]
        private void NotifyPlayerInitializer()
        {
            // Находим PlayerInitializer владельца и уведомляем его
            foreach (var client in NetworkManager.ServerManager.Clients.Values)
            {
                if (client.FirstObject != null)
                {
                    var initializer = client.FirstObject.GetComponent<PlayerInitializer>();
                    if (initializer != null && initializer.OwnerId == OwnerId)
                    {
                        initializer.OnLifeFruitDestroyed();
                        break;
                    }
                }
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnDeath -= OnDeath;
            }
            
            PlayerInitializer.OnPlayerFogReady -= ApplyFog;
        }
    }
}
