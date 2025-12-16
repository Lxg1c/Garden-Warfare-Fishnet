using FishNet.Object;
using FishNet.Object.Synchronizing;
using Core.Components;
using Gameplay;
using Gameplay.TurretPlant;
using UnityEngine;
using Weapon;

namespace Player
{
    public class PlayerCarryController : NetworkBehaviour
    {
        [Header("Interaction Settings")]
        [SerializeField] private float interactRange = 2.5f;
        [SerializeField] private float digTime = 1.5f;
        [SerializeField] private LayerMask turretPlantLayer;

        [Header("Carry Settings")]
        [SerializeField] private float carrySpeedMultiplier = 0.7f;
        [SerializeField] private Vector3 carryOffset = new Vector3(0, 0.5f, 1.2f);
        [SerializeField] private Vector3 lifeFruitCarryOffset = new Vector3(0, 1f, 0.8f);

        [Header("LifeFruit Layer")]
        [SerializeField] private LayerMask lifeFruitLayer;

        [Header("Ghost Preview")]
        [SerializeField] private GameObject plantGhostPrefab;
        [SerializeField] private Material ghostValidMaterial;
        [SerializeField] private Material ghostInvalidMaterial;
        [SerializeField] private float ghostForwardOffset = 2f;
        [SerializeField] private float ghostHeightOffset = 0.1f; // Небольшой отступ от земли

        // ==================
        // Ghost Preview
        // ==================

        private GameObject _ghostInstance;
        private Renderer[] _ghostRenderers;
        private Vector3 _lastGhostPosition; // Кэшируем позицию для посадки

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<NetworkObject> _carriedPlantNetObj = new();
        private readonly SyncVar<NetworkObject> _carriedLifeFruitNetObj = new();

        // ==================
        // Локальные данные
        // ==================

        private PlayerInputActions _input;
        private PlayerMovement _playerMovement;
        private WeaponController _weaponController;
        private Health _health;

        private TurretPlant _nearbyPlant;
        private LifeFruit _nearbyLifeFruit;
        private float _digProgress;
        private float _lifeFruitDigProgress;
        private float _originalSpeed;

        // Кэш для LifeFruit владельца
        private LifeFruit _myLifeFruit;

        // Что сейчас выкапываем
        private enum DigTarget { None, Plant, LifeFruit }
        private DigTarget _currentDigTarget = DigTarget.None;

        // ==================
        // Properties
        // ==================

        public bool IsCarryingPlant => _carriedPlantNetObj.Value != null;
        public bool IsCarryingLifeFruit => _carriedLifeFruitNetObj.Value != null;
        public bool IsCarrying => IsCarryingPlant || IsCarryingLifeFruit;

        // ==================
        // Lifecycle
        // ==================

        private void Awake()
        {
            _input = new PlayerInputActions();
            _playerMovement = GetComponent<PlayerMovement>();
            _weaponController = GetComponent<WeaponController>();
            _health = GetComponent<Health>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Подписываемся на урон - роняем предмет при получении урона
            if (_health != null)
            {
                _health.OnDamaged += OnPlayerDamaged;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            if (_health != null)
            {
                _health.OnDamaged -= OnPlayerDamaged;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (IsOwner)
            {
                _input.Enable();
                _originalSpeed = _playerMovement.normalSpeed;
            }

            _carriedPlantNetObj.OnChange += OnCarriedPlantChanged;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (IsOwner)
            {
                _input.Disable();
                HidePlantingZone();
            }

            _carriedPlantNetObj.OnChange -= OnCarriedPlantChanged;
        }

        /// <summary>
        /// Вызывается при получении урона - роняем переносимый предмет
        /// </summary>
        [Server]
        private void OnPlayerDamaged(Transform attacker)
        {
            if (!IsCarrying) return;

            Debug.Log($"[PlayerCarryController] Player {OwnerId} took damage while carrying - dropping item");

            Vector3 dropPos = transform.position + transform.forward * 1.5f;

            // Находим позицию на земле
            if (Physics.Raycast(dropPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
            {
                dropPos = hit.point;
            }

            if (IsCarryingPlant)
            {
                var plant = _carriedPlantNetObj.Value?.GetComponent<TurretPlant>();
                if (plant != null)
                {
                    plant.Drop(dropPos);
                }
                _carriedPlantNetObj.Value = null;
            }
            else if (IsCarryingLifeFruit)
            {
                var lifeFruit = _carriedLifeFruitNetObj.Value?.GetComponent<LifeFruit>();
                if (lifeFruit != null)
                {
                    lifeFruit.Drop(dropPos);
                }
                _carriedLifeFruitNetObj.Value = null;
            }

            ApplyCarryStateObserversRpc(false);
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (IsCarrying)
            {
                HandleCarryingInput();

                // Показываем зону и ghost preview
                if (IsCarryingPlant)
                {
                    ShowPlantingZone();
                    UpdateGhostPreview(true);
                }
                else if (IsCarryingLifeFruit)
                {
                    // Показываем зону куда можно посадить LifeFruit
                    ShowLifeFruitPlantingZone();
                    UpdateGhostPreview(false);
                }
            }
            else
            {
                HandleDigInput();
                HidePlantingZone();
                HideLifeFruitPlantingZone();
                HideGhostPreview();
            }
        }

        // LateUpdate больше не нужен - растение теперь child объект игрока
        // и позиция синхронизируется автоматически

        // ==================
        // DIGGING (выкапывание)
        // ==================

        private void HandleDigInput()
        {
            // Ищем ближайший объект для взаимодействия
            _nearbyPlant = FindNearbyWildPlant();
            _nearbyLifeFruit = FindNearbyLifeFruit();

            // Определяем что ближе - растение или LifeFruit
            float plantDist = _nearbyPlant != null
                ? Vector3.Distance(transform.position, _nearbyPlant.transform.position)
                : float.MaxValue;
            float lifeFruitDist = _nearbyLifeFruit != null
                ? Vector3.Distance(transform.position, _nearbyLifeFruit.transform.position)
                : float.MaxValue;

            // Определяем цель
            DigTarget newTarget = DigTarget.None;
            if (plantDist < lifeFruitDist && _nearbyPlant != null)
            {
                newTarget = DigTarget.Plant;
            }
            else if (_nearbyLifeFruit != null)
            {
                newTarget = DigTarget.LifeFruit;
            }

            // Сбрасываем прогресс если сменилась цель
            if (newTarget != _currentDigTarget)
            {
                _digProgress = 0f;
                _lifeFruitDigProgress = 0f;
                _currentDigTarget = newTarget;
            }

            if (_currentDigTarget == DigTarget.None)
            {
                return;
            }

            // Зажатие E
            if (_input.Player.Interact.IsPressed())
            {
                if (_currentDigTarget == DigTarget.Plant)
                {
                    _digProgress += Time.deltaTime;

                    if (_digProgress >= digTime)
                    {
                        PickupPlantServerRpc(_nearbyPlant.NetworkObject);
                        _digProgress = 0f;
                        _currentDigTarget = DigTarget.None;
                    }
                }
                else if (_currentDigTarget == DigTarget.LifeFruit)
                {
                    float requiredTime = _nearbyLifeFruit.GetPickupTime(OwnerId);
                    _lifeFruitDigProgress += Time.deltaTime;

                    if (_lifeFruitDigProgress >= requiredTime)
                    {
                        PickupLifeFruitServerRpc(_nearbyLifeFruit.NetworkObject);
                        _lifeFruitDigProgress = 0f;
                        _currentDigTarget = DigTarget.None;
                    }
                }
            }
            else
            {
                _digProgress = 0f;
                _lifeFruitDigProgress = 0f;
            }
        }

        private TurretPlant FindNearbyWildPlant()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, turretPlantLayer);

            TurretPlant closest = null;
            float closestDist = float.MaxValue;

            foreach (var c in hits)
            {
                var plant = c.GetComponent<TurretPlant>();
                if (plant != null && plant.CanBePickedUp)
                {
                    float dist = Vector3.Distance(transform.position, plant.transform.position);
                    if (dist < closestDist)
                    {
                        closest = plant;
                        closestDist = dist;
                    }
                }
            }

            return closest;
        }

        private LifeFruit FindNearbyLifeFruit()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, lifeFruitLayer);

            LifeFruit closest = null;
            float closestDist = float.MaxValue;

            foreach (var c in hits)
            {
                var lifeFruit = c.GetComponent<LifeFruit>();
                if (lifeFruit != null && lifeFruit.CanBePickedUp(OwnerId))
                {
                    float dist = Vector3.Distance(transform.position, lifeFruit.transform.position);
                    if (dist < closestDist)
                    {
                        closest = lifeFruit;
                        closestDist = dist;
                    }
                }
            }

            return closest;
        }

        // ==================
        // CARRYING (переноска)
        // ==================

        private void HandleCarryingInput()
        {
            // G - бросить
            if (_input.Player.Drop.WasPressedThisFrame())
            {
                if (IsCarryingPlant)
                {
                    DropPlantServerRpc();
                }
                else if (IsCarryingLifeFruit)
                {
                    DropLifeFruitServerRpc();
                }
                return;
            }

            // E или LMB - посадить
            if (_input.Player.Interact.WasPressedThisFrame() || _input.Player.Fire.WasPressedThisFrame())
            {
                Vector3 plantPos = GetPlantingPosition();

                if (IsCarryingPlant)
                {
                    TryPlantServerRpc(plantPos);
                }
                else if (IsCarryingLifeFruit)
                {
                    TryPlantLifeFruitServerRpc(plantPos);
                }
            }
        }

        private Vector3 GetPlantingPosition()
        {
            // Используем кэшированную позицию ghost preview если она есть
            if (_lastGhostPosition != Vector3.zero)
            {
                return _lastGhostPosition;
            }

            // Fallback - вычисляем позицию
            return CalculatePlantingPosition();
        }

        private Vector3 CalculatePlantingPosition()
        {
            Vector3 pos = transform.position + transform.forward * ghostForwardOffset;

            // Raycast вниз чтобы найти землю
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point + Vector3.up * ghostHeightOffset;
            }

            return pos;
        }

        // ==================
        // PLANTING ZONE (зона посадки)
        // ==================

        private void ShowPlantingZone()
        {
            // Находим свой LifeFruit (кэшируем)
            if (_myLifeFruit == null)
            {
                _myLifeFruit = TurretPlantManager.Instance?.FindLifeFruitForPlayer(OwnerId);
            }

            if (_myLifeFruit != null)
            {
                // Включаем зону через родительский объект LifeFruit
                _myLifeFruit.SendMessage("ShowPlantingZone", SendMessageOptions.DontRequireReceiver);
            }
        }

        private void HidePlantingZone()
        {
            if (_myLifeFruit != null)
            {
                _myLifeFruit.SendMessage("HidePlantingZone", SendMessageOptions.DontRequireReceiver);
            }
        }

        // ==================
        // LIFEFRUIT PLANTING ZONE
        // ==================

        private void ShowLifeFruitPlantingZone()
        {
            // Для LifeFruit показываем ту же зону (вокруг своего LifeFruit если есть)
            if (_myLifeFruit == null)
            {
                _myLifeFruit = TurretPlantManager.Instance?.FindLifeFruitForPlayer(OwnerId);
            }

            if (_myLifeFruit != null)
            {
                _myLifeFruit.SendMessage("ShowPlantingZone", SendMessageOptions.DontRequireReceiver);
            }
        }

        private void HideLifeFruitPlantingZone()
        {
            if (_myLifeFruit != null)
            {
                _myLifeFruit.SendMessage("HidePlantingZone", SendMessageOptions.DontRequireReceiver);
            }
        }

        // ==================
        // GHOST PREVIEW
        // ==================

        private void UpdateGhostPreview(bool isPlant)
        {
            // Создаём ghost если нужно
            if (_ghostInstance == null && plantGhostPrefab != null)
            {
                _ghostInstance = Instantiate(plantGhostPrefab);
                _ghostRenderers = _ghostInstance.GetComponentsInChildren<Renderer>();

                // Отключаем все компоненты кроме рендереров
                foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>())
                {
                    col.enabled = false;
                }
                foreach (var behaviour in _ghostInstance.GetComponentsInChildren<MonoBehaviour>())
                {
                    behaviour.enabled = false;
                }
            }

            if (_ghostInstance == null) return;

            // Вычисляем позицию для ghost
            Vector3 plantPos = CalculatePlantingPosition();
            _lastGhostPosition = plantPos; // Кэшируем для использования при посадке

            // Позиционируем ghost
            _ghostInstance.transform.position = plantPos;

            // Поворачиваем ghost в направлении взгляда игрока (только по Y)
            Vector3 forward = transform.forward;
            forward.y = 0;
            if (forward != Vector3.zero)
            {
                _ghostInstance.transform.rotation = Quaternion.LookRotation(forward);
            }

            _ghostInstance.SetActive(true);

            // Проверяем валидность позиции
            bool isValid;
            if (isPlant)
            {
                // Для растения - проверяем через TurretPlantManager
                isValid = TurretPlantManager.Instance != null &&
                          TurretPlantManager.Instance.CanPlantAt(OwnerId, plantPos);
            }
            else
            {
                // Для LifeFruit - проверяем расстояние до своего LifeFruit
                isValid = CanPlantLifeFruitAt(plantPos);
            }

            // Меняем материал в зависимости от валидности
            UpdateGhostMaterial(isValid);
        }

        private void UpdateGhostMaterial(bool isValid)
        {
            if (_ghostRenderers == null) return;

            Material mat = isValid ? ghostValidMaterial : ghostInvalidMaterial;
            if (mat == null) return;

            foreach (var renderer in _ghostRenderers)
            {
                renderer.material = mat;
            }
        }

        private void HideGhostPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.SetActive(false);
            }
            _lastGhostPosition = Vector3.zero;
        }

        private bool CanPlantLifeFruitAt(Vector3 position)
        {
            // LifeFruit можно посадить ТОЛЬКО в его оригинальной позиции
            if (!IsCarryingLifeFruit) return false;

            var carriedLifeFruit = _carriedLifeFruitNetObj.Value?.GetComponent<LifeFruit>();
            if (carriedLifeFruit == null) return false;

            // Проверяем расстояние до оригинальной позиции
            Vector3 originalPos = carriedLifeFruit.OriginalPosition;
            float dist = Vector3.Distance(position, originalPos);
            return dist <= 3f; // Небольшой допуск
        }

        private void OnDestroy()
        {
            if (_ghostInstance != null)
            {
                Destroy(_ghostInstance);
            }
        }

        // ==================
        // SERVER RPCs
        // ==================

        [ServerRpc]
        private void PickupPlantServerRpc(NetworkObject plantNetObj)
        {
            if (plantNetObj == null) return;
            if (IsCarrying) return;

            var plant = plantNetObj.GetComponent<TurretPlant>();
            if (plant == null || !plant.CanBePickedUp) return;

            if (plant.TryPickup(OwnerId))
            {
                _carriedPlantNetObj.Value = plantNetObj;

                // Привязываем растение к игроку (делаем дочерним объектом)
                plant.AttachToPlayer(transform, carryOffset);

                ApplyCarryStateObserversRpc(true);
            }
        }

        [ServerRpc]
        private void DropPlantServerRpc()
        {
            if (!IsCarrying) return;

            var plant = _carriedPlantNetObj.Value.GetComponent<TurretPlant>();
            if (plant != null)
            {
                Vector3 dropPos = transform.position + transform.forward * 1.5f;
                // TurretPlant.Drop сам найдёт правильную высоту через GetGroundPosition
                plant.Drop(dropPos);
            }

            _carriedPlantNetObj.Value = null;
            ApplyCarryStateObserversRpc(false);
        }

        [ServerRpc]
        private void TryPlantServerRpc(Vector3 position)
        {
            if (!IsCarryingPlant) return;

            var manager = TurretPlantManager.Instance;
            if (manager == null || !manager.CanPlantAt(OwnerId, position))
            {
                NotifyCannotPlantTargetRpc(Owner);
                return;
            }

            var plant = _carriedPlantNetObj.Value.GetComponent<TurretPlant>();
            if (plant != null)
            {
                plant.Plant(OwnerId, position);
            }

            _carriedPlantNetObj.Value = null;
            ApplyCarryStateObserversRpc(false);
        }

        // ==================
        // LIFEFRUIT SERVER RPCs
        // ==================

        [ServerRpc]
        private void PickupLifeFruitServerRpc(NetworkObject lifeFruitNetObj)
        {
            if (lifeFruitNetObj == null) return;
            if (IsCarrying) return;

            var lifeFruit = lifeFruitNetObj.GetComponent<LifeFruit>();
            if (lifeFruit == null || !lifeFruit.CanBePickedUp(OwnerId)) return;

            if (lifeFruit.TryPickup(OwnerId))
            {
                _carriedLifeFruitNetObj.Value = lifeFruitNetObj;

                // Привязываем LifeFruit к игроку
                lifeFruit.AttachToPlayer(transform, lifeFruitCarryOffset);

                ApplyCarryStateObserversRpc(true);
                Debug.Log($"[Server] Player {OwnerId} picked up LifeFruit of player {lifeFruit.OwnerId}");
            }
        }

        [ServerRpc]
        private void DropLifeFruitServerRpc()
        {
            if (!IsCarryingLifeFruit) return;

            var lifeFruit = _carriedLifeFruitNetObj.Value.GetComponent<LifeFruit>();
            if (lifeFruit != null)
            {
                Vector3 dropPos = transform.position + transform.forward * 1.5f;

                if (Physics.Raycast(dropPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
                {
                    dropPos = hit.point;
                }

                lifeFruit.Drop(dropPos);
            }

            _carriedLifeFruitNetObj.Value = null;
            ApplyCarryStateObserversRpc(false);
        }

        [ServerRpc]
        private void TryPlantLifeFruitServerRpc(Vector3 position)
        {
            if (!IsCarryingLifeFruit) return;

            var lifeFruit = _carriedLifeFruitNetObj.Value.GetComponent<LifeFruit>();
            if (lifeFruit == null) return;

            // LifeFruit можно посадить ТОЛЬКО в его оригинальной позиции
            Vector3 originalPos = lifeFruit.OriginalPosition;
            float distToOriginal = Vector3.Distance(position, originalPos);

            // Проверяем расстояние до оригинальной позиции (небольшой допуск)
            if (distToOriginal > 3f)
            {
                Debug.Log($"[PlayerCarryController] Cannot plant LifeFruit - too far from original position ({distToOriginal:F1}m)");
                NotifyCannotPlantTargetRpc(Owner);
                return;
            }

            // Сажаем LifeFruit в оригинальной позиции и делаем его своим
            lifeFruit.PlantForNewOwner(OwnerId, originalPos);

            _carriedLifeFruitNetObj.Value = null;
            ApplyCarryStateObserversRpc(false);
        }

        // ==================
        // RPCs
        // ==================

        [ObserversRpc]
        private void ApplyCarryStateObserversRpc(bool carrying)
        {
            if (_playerMovement != null)
            {
                _playerMovement.normalSpeed = carrying
                    ? _originalSpeed * carrySpeedMultiplier
                    : _originalSpeed;
            }

            if (_weaponController != null)
            {
                _weaponController.SetWeaponEnabled(!carrying);
            }
        }

        [TargetRpc]
        private void NotifyCannotPlantTargetRpc(FishNet.Connection.NetworkConnection _)
        {
            Debug.Log("[PlayerCarryController] Cannot plant here!");
        }

        // ==================
        // SYNC CALLBACKS
        // ==================

        private void OnCarriedPlantChanged(NetworkObject oldValue, NetworkObject newValue, bool asServer)
        {
            if (!IsOwner) return;

            if (newValue == null)
            {
                HidePlantingZone();
                _myLifeFruit = null; // Сбрасываем кэш
            }
        }

        // ==================
        // GIZMOS
        // ==================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
