using AI.Plant;
using FishNet.Object; // FishNet namespace
using UnityEngine;

namespace Player.Components
{
    public class CarryPlantAgent : NetworkBehaviour
    {
        [Header("Carry Settings")]
        public float snapGridSize = 1f;

        // Свойство для локальной проверки, но состояние хранит сервер
        public bool IsCarrying => _carriedPlant != null;

        private Plant _carriedPlant;
        public Plant CarriedPlant => _carriedPlant;

        // ----------------------------------------------------
        // CLIENT SIDE (Ввод игрока)
        // ----------------------------------------------------

        public void PickupPlant(Plant plant)
        {
            if (IsCarrying) return;
            if (!IsOwner) return;
            
            NetworkObject plantNO = plant.GetComponent<NetworkObject>();
            if (plantNO != null)
            {
                ServerPickupPlant(plantNO);
            }
        }

        public void DropPlant()
        {
            if (!IsCarrying) return;
            if (!IsOwner) return;

            ServerDropPlant();
        }

        public bool PlacePlant(Vector3 position, Quaternion rotation)
        {
            if (!IsCarrying) return false;
            if (!IsOwner) return false;

            ServerPlacePlant(position, rotation);
            return true;
        }

        public Vector3 SnapToGrid(Vector3 position)
        {
            return new Vector3(
                Mathf.Round(position.x / snapGridSize) * snapGridSize,
                position.y,
                Mathf.Round(position.z / snapGridSize) * snapGridSize
            );
        }

        // ----------------------------------------------------
        // SERVER SIDE (Логика и синхронизация)
        // ----------------------------------------------------

        [ServerRpc]
        private void ServerPickupPlant(NetworkObject plantNO)
        {
            if (plantNO == null) return;
            Plant plant = plantNO.GetComponent<Plant>();
            if (plant == null) return;

            // 1. Сообщаем растению, что его несут (изменится SyncVar -> отключится физика у всех)
            plant.SetCarried(this.NetworkObject);

            // 2. Сетевое парентирование (FishNet синхронизирует привязку к игроку)
            plantNO.SetParent(this.NetworkObject);

            // 3. Локальная позиция на сервере (NetworkTransform синхронизирует это клиентам)
            plant.transform.localPosition = Vector3.zero;
            plant.transform.localRotation = Quaternion.identity;

            // Запоминаем ссылку
            _carriedPlant = plant;
        }

        [ServerRpc]
        private void ServerDropPlant()
        {
            if (_carriedPlant == null) return;

            // 1. Отвязываем родителя
            NetworkObject plantNO = _carriedPlant.GetComponent<NetworkObject>();
            plantNO.UnsetParent();

            // 2. Сообщаем растению о сбросе (изменится SyncVar -> включится физика)
            _carriedPlant.Drop();

            _carriedPlant = null;
        }

        [ServerRpc]
        private void ServerPlacePlant(Vector3 position, Quaternion rotation)
        {
            if (_carriedPlant == null) return;

            // 1. Отвязываем родителя
            NetworkObject plantNO = _carriedPlant.GetComponent<NetworkObject>();
            plantNO.UnsetParent();

            // 2. Вызываем метод установки (Plant сам телепортируется и включит турель)
            _carriedPlant.Place(position, rotation.eulerAngles.y);

            _carriedPlant = null;
        }
    }
}