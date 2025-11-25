using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Player.Components
{
    public class PlayerInfo : NetworkBehaviour
    {
        // В FishNet v4 мы используем SyncVar<T> вместо атрибута [SyncVar]
        // readonly здесь означает, что мы не меняем саму ссылку на переменную, но меняем её .Value
        public readonly SyncVar<int> ActorNumber = new SyncVar<int>();

        private Transform _spawnPoint;

        private void Awake()
        {
            // Подписываемся на изменение переменной
            ActorNumber.OnChange += OnActorNumberChanged;
        }

        private void OnDestroy()
        {
            // Хорошая практика - отписываться
            ActorNumber.OnChange -= OnActorNumberChanged;
        }

        // Этот метод вызывает GameSpawnManager на сервере
        public void SetActorNumber(int id)
        {
            // ВАЖНО: В v4 мы меняем .Value
            ActorNumber.Value = id;
        }

        public void SetSpawnPoint(Transform point)
        {
            _spawnPoint = point;
        }

        // Метод, который срабатывает при изменении значения (и на сервере, и на клиенте)
        // Синтаксис события изменился: (старое значение, новое значение, это сервер?)
        private void OnActorNumberChanged(int oldVal, int newVal, bool asServer)
        {
            Debug.Log($"[PlayerInfo] ID изменен. Старый: {oldVal}, Новый: {newVal}");

            // Здесь можно обновить визуальные элементы, если нужно
            // Например: gameObject.name = $"Player_{newVal}";
        }
    }
}