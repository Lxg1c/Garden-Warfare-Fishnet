using AI.Plant;
using FishNet.Object;
using FishNet.Object.Synchronizing; // Для SyncVar
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapon; // Ссылка на WeaponController

namespace Player.Components
{
    public class CarryPlantAgent : NetworkBehaviour
    {
        [Header("Settings")]
        public float pickupRange = 3f;

        [Tooltip("Тег растения для поиска (должен совпадать с тегом на префабе)")]
        public string targetPlantTag = "Plant";

        [Tooltip("Точка в руках, куда прилипнет растение (создайте пустой объект в иерархии игрока)")]
        public Transform carryPoint;

        // Синхронизируемая переменная: Несем мы что-то или нет?
        public readonly SyncVar<bool> IsCarryingSync = new SyncVar<bool>();

        // Локальное свойство
        public bool IsCarrying => IsCarryingSync.Value;

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private WeaponController weaponController;

        private Plant _carriedPlant;
        private PlayerInputActions _inputActions;

        private void Awake()
        {
            _inputActions = new PlayerInputActions();

            // Автопоиск ссылок
            if (animator == null) animator = GetComponent<Animator>();
            if (weaponController == null) weaponController = GetComponent<WeaponController>();

            // Подписываемся на изменение переменной (сработает у всех игроков)
            IsCarryingSync.OnChange += OnCarryingStateChanged;
        }

        private void OnDestroy()
        {
            IsCarryingSync.OnChange -= OnCarryingStateChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) _inputActions.Enable();

            // Принудительно обновляем визуал при входе
            UpdateVisuals(IsCarryingSync.Value);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) _inputActions.Disable();
        }

        // === ГЛАВНАЯ ЛОГИКА АНИМАЦИИ ===

        private void OnCarryingStateChanged(bool prev, bool next, bool asServer)
        {
            UpdateVisuals(next);
        }

        private void UpdateVisuals(bool isCarrying)
        {
            // 1. Переключаем параметр в Аниматоре
            if (animator != null)
            {
                animator.SetBool("IsCarrying", isCarrying);
            }

            // 2. Скрываем/Показываем оружие через WeaponController
            if (weaponController != null)
            {
                weaponController.ToggleWeaponVisuals(!isCarrying);
            }
        }

        // === ЛОГИКА ВВОДА ===

        private void Update()
        {
            if (!IsOwner) return;

            // Если руки пусты -> Interact (Поднять)
            if (_carriedPlant == null)
            {
                if (_inputActions.Player.Interact.WasPressedThisFrame()) TryPickup();
            }
            // Если несем -> Place (Поставить) или Drop (Бросить)
            else
            {
                if (_inputActions.Player.Place.WasPressedThisFrame()) TryPlace();
                if (_inputActions.Player.Drop.WasPressedThisFrame()) TryDrop();
            }
        }

        private void TryPickup()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, pickupRange);
            foreach (var hit in hits)
            {
                if (hit.CompareTag(targetPlantTag) || hit.GetComponentInParent<Plant>())
                {
                    var plant = hit.GetComponentInParent<Plant>();
                    if (plant != null)
                    {
                        bool isNeutral = plant.CurrentState.Value == Plant.State.Neutral;
                        bool isMyPlaced = plant.CurrentState.Value == Plant.State.Placed &&
                                          plant.OwnerActorNumber.Value == OwnerId;

                        if (isNeutral || isMyPlaced)
                        {
                            ServerPickupPlant(plant.GetComponent<NetworkObject>());
                            return;
                        }
                    }
                }
            }
        }

        private void TryPlace()
        {
            BaseArea foundBase = null;
            Collider[] hits = Physics.OverlapSphere(transform.position, 10f);

            foreach (var h in hits)
            {
                var area = h.GetComponentInParent<BaseArea>();
                if (area != null && area.OwnerId == OwnerId)
                {
                    if (Vector3.Distance(transform.position, area.transform.position) <= area.Radius)
                    {
                        foundBase = area;
                        break;
                    }
                }
            }

            if (foundBase != null)
            {
                Vector3 placePos = transform.position + transform.forward * 1.0f;
                placePos.y = transform.position.y;
                ServerPlacePlant(placePos, transform.rotation, foundBase.OwnerId);
            }
            else
            {
                Debug.Log("Нет базы рядом для установки.");
            }
        }

        private void TryDrop()
        {
            ServerDropPlant();
        }

        // --- SERVER RPCs (Меняют состояние) ---

        [ServerRpc]
        private void ServerPickupPlant(NetworkObject plantNO)
        {
            if (plantNO == null) return;
            Plant plant = plantNO.GetComponent<Plant>();

            if (plant != null)
            {
                if (plant.CurrentState.Value == Plant.State.Neutral ||
                   (plant.CurrentState.Value == Plant.State.Placed && plant.OwnerActorNumber.Value == OwnerId))
                {
                    plant.SetCarried(this.NetworkObject);
                    plantNO.SetParent(this.NetworkObject);

                    // === ИСПРАВЛЕНИЕ ПОЗИЦИИ В РУКАХ ===
                    if (carryPoint != null)
                    {
                        // Используем точку переноски (локально относительно игрока)
                        plant.transform.localPosition = carryPoint.localPosition;
                        plant.transform.localRotation = Quaternion.identity;
                    }
                    else
                    {
                        // Дефолтная позиция (если carryPoint не назначен)
                        plant.transform.localPosition = new Vector3(0, 1.5f, 0.5f);
                        plant.transform.localRotation = Quaternion.identity;
                    }
                    // ===================================

                    _carriedPlant = plant;

                    // Меняем переменную -> Это вызовет OnCarryingStateChanged -> Скроет оружие
                    IsCarryingSync.Value = true;

                    TargetSetPlant(base.Owner, plant);
                }
            }
        }

        [ServerRpc]
        private void ServerDropPlant()
        {
            if (_carriedPlant == null) return;

            NetworkObject plantNO = _carriedPlant.GetComponent<NetworkObject>();
            plantNO.UnsetParent();

            _carriedPlant.Drop();

            // Немного вперед и вверх, чтобы не провалилось
            _carriedPlant.transform.position = transform.position + transform.forward + Vector3.up * 0.5f;

            _carriedPlant = null;

            // Сбрасываем переменную -> Покажет оружие
            IsCarryingSync.Value = false;

            TargetSetPlant(base.Owner, null);
        }

        [ServerRpc]
        private void ServerPlacePlant(Vector3 pos, Quaternion rot, int ownerId)
        {
            if (_carriedPlant == null) return;

            NetworkObject plantNO = _carriedPlant.GetComponent<NetworkObject>();
            plantNO.UnsetParent();

            _carriedPlant.Place(pos, rot.eulerAngles.y, ownerId);

            _carriedPlant = null;

            // Сбрасываем переменную
            IsCarryingSync.Value = false;

            TargetSetPlant(base.Owner, null);
        }

        [TargetRpc]
        private void TargetSetPlant(FishNet.Connection.NetworkConnection conn, Plant plant)
        {
            _carriedPlant = plant;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, pickupRange);
        }
    }
}