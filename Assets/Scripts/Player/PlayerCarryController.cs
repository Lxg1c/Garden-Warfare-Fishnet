using FishNet.Object;
using FishNet.Object.Synchronizing;
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

        // ==================
        // Синхронизация
        // ==================

        private readonly SyncVar<NetworkObject> _carriedPlantNetObj = new();

        // ==================
        // Локальные данные
        // ==================

        private PlayerInputActions _input;
        private PlayerMovement _playerMovement;
        private WeaponController _weaponController;

        private TurretPlant _nearbyPlant;
        private float _digProgress;
        private float _originalSpeed;

        // Кэш для LifeFruit владельца
        private LifeFruit _myLifeFruit;

        // ==================
        // Properties
        // ==================

        public bool IsCarrying => _carriedPlantNetObj.Value != null;

        // ==================
        // Lifecycle
        // ==================

        private void Awake()
        {
            _input = new PlayerInputActions();
            _playerMovement = GetComponent<PlayerMovement>();
            _weaponController = GetComponent<WeaponController>();
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

        private void Update()
        {
            if (!IsOwner) return;

            if (IsCarrying)
            {
                HandleCarryingInput();
                ShowPlantingZone();
            }
            else
            {
                HandleDigInput();
                HidePlantingZone();
            }
        }

        private void LateUpdate()
        {
            if (!IsServerStarted) return;

            if (IsCarrying)
            {
                UpdateCarriedPlantPosition();
            }
        }

        // ==================
        // DIGGING (выкапывание)
        // ==================

        private void HandleDigInput()
        {
            _nearbyPlant = FindNearbyWildPlant();

            if (_nearbyPlant == null)
            {
                _digProgress = 0f;
                return;
            }

            // Зажатие E
            if (_input.Player.Interact.IsPressed())
            {
                _digProgress += Time.deltaTime;

                if (_digProgress >= digTime)
                {
                    PickupPlantServerRpc(_nearbyPlant.NetworkObject);
                    _digProgress = 0f;
                }
            }
            else
            {
                _digProgress = 0f;
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

        // ==================
        // CARRYING (переноска)
        // ==================

        private void HandleCarryingInput()
        {
            // G - бросить
            if (_input.Player.Drop.WasPressedThisFrame())
            {
                DropPlantServerRpc();
                return;
            }

            // E или LMB - посадить
            if (_input.Player.Interact.WasPressedThisFrame() || _input.Player.Fire.WasPressedThisFrame())
            {
                Vector3 plantPos = GetPlantingPosition();
                TryPlantServerRpc(plantPos);
            }
        }

        private void UpdateCarriedPlantPosition()
        {
            if (_carriedPlantNetObj.Value == null) return;

            var plant = _carriedPlantNetObj.Value.GetComponent<TurretPlant>();
            if (plant == null) return;

            Vector3 carryPos = transform.position +
                               transform.forward * carryOffset.z +
                               transform.up * carryOffset.y +
                               transform.right * carryOffset.x;

            plant.UpdateCarriedPosition(carryPos, transform.rotation);
        }

        private Vector3 GetPlantingPosition()
        {
            Vector3 pos = transform.position + transform.forward * 1.5f;

            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
            {
                pos = hit.point;
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
                // Включаем visualRadius только на своём LifeFruit
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

                if (Physics.Raycast(dropPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
                {
                    dropPos = hit.point;
                }

                plant.Drop(dropPos);
            }

            _carriedPlantNetObj.Value = null;
            ApplyCarryStateObserversRpc(false);
        }

        [ServerRpc]
        private void TryPlantServerRpc(Vector3 position)
        {
            if (!IsCarrying) return;

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
        private void NotifyCannotPlantTargetRpc(FishNet.Connection.NetworkConnection conn)
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
