using Core.Components;
using FishNet.Object;
using UnityEngine;

namespace AI.Plant
{
    public class PlantProjectile : NetworkBehaviour
    {
        [Header("Settings")]
        public float speed = 20f;
        public float lifetime = 3f;

        private int _damage;
        private int _ownerId; // ID игрока, чья турель выпустила пулю

        public void Initialize(int dmg, int ownerId)
        {
            _damage = dmg;
            _ownerId = ownerId;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Уничтожаем пулю через время (только на сервере)
            Invoke(nameof(DestroySelf), lifetime);
        }

        private void Update()
        {
            // Движение только на сервере (для простоты)
            // Если нужно супер-плавное движение, добавьте NetworkTransform на префаб
            if (IsServerInitialized)
            {
                transform.Translate(Vector3.forward * speed * Time.deltaTime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Обработка столкновений только на сервере
            if (!IsServerInitialized) return;

            // Игнорируем само растение и другие пули
            if (other.GetComponent<Plant>() || other.GetComponent<PlantProjectile>()) return;

            // Если попали в кого-то со здоровьем
            if (other.TryGetComponent(out Health health))
            {
                // Проверка: не стреляем по владельцу турели
                if (other.TryGetComponent(out NetworkObject targetNO))
                {
                    if (targetNO.OwnerId == _ownerId) return;
                }

                health.TakeDamage(_damage, transform);
                DestroySelf();
            }
            // Если попали в стену (не триггер)
            else if (!other.isTrigger)
            {
                DestroySelf();
            }
        }

        private void DestroySelf()
        {
            // Важно: деспавним через FishNet, а не Destroy()
            if (NetworkObject.IsSpawned)
                NetworkObject.Despawn();
            else
                Destroy(gameObject);
        }
    }
}