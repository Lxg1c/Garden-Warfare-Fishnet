using Core.Components;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

namespace Weapon.Projectile
{
    [RequireComponent(typeof(Rigidbody))]
    public class Bullet : NetworkBehaviour
    {
        [Header("Bullet Settings")]
        [SerializeField] private int damage = 10;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float speed = 20f;

        private Transform _owner;
        private Rigidbody _rb;

        public void SetOwner(NetworkConnection owner)
        {
            if (owner != null && owner.FirstObject != null)
                _owner = owner.FirstObject.transform;
        }

        public void SetDamage(float newDamage)
        {
            damage = Mathf.RoundToInt(newDamage);
        }

        public override void OnStartServer()
        {
            base.OnStartServer(); // Хорошая практика вызывать base
            _rb = GetComponent<Rigidbody>();
            Invoke(nameof(DespawnBullet), lifetime);
        }

        private void FixedUpdate()
        {
            // Пуля двигается только на сервере (если это серверная пуля)
            // Если вы хотите, чтобы клиенты тоже видели плавное движение, 
            // пуле нужен компонент NetworkTransform или NetworkTransform(Predicted)
            if (!IsServerInitialized) return;

            _rb.MovePosition(transform.position + transform.forward * speed * Time.fixedDeltaTime);
        }

        private void OnTriggerEnter(Collider other)
        {
            // Обработка столкновений только на сервере
            if (!IsServerInitialized) return;

            // Игнорируем столкновение с владельцем пули
            if (_owner != null && other.transform == _owner) return;

            if (other.TryGetComponent(out Health hp))
            {
                // ИСПРАВЛЕНИЕ:
                // 1. Метод называется TakeDamage.
                // 2. Вторым аргументом передаем _owner (Transform), чтобы Health знал, кто атаковал.
                hp.TakeDamage(damage, _owner);
            }

            DespawnBullet();
        }

        [Server]
        private void DespawnBullet()
        {
            // Проверка на null нужна, так как объект мог быть уже уничтожен
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }
    }
}