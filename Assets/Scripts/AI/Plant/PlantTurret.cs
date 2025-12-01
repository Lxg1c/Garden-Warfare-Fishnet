using Core.Components;
using FishNet.Object;
using UnityEngine;

namespace AI.Plant
{
    public class PlantTurret : NetworkBehaviour
    {
        [Header("Combat Settings")]
        public float attackRange = 10f;
        public float fireRate = 1f;
        public int damage = 10;

        // УБРАЛИ LayerMask enemyLayer

        [Header("Visuals")]
        public Transform turretHead;
        public Transform firePoint;
        public GameObject projectilePrefab;

        private float _nextFireTime;
        private Transform _currentTarget;
        private Plant _parentPlant;

        private void Awake()
        {
            _parentPlant = GetComponent<Plant>();
            enabled = false; // Выключена по умолчанию (включается из Plant.cs при установке)
        }

        private void Update()
        {
            // Стреляем только на сервере
            if (!IsServerInitialized) return;

            // Если цели нет или она стала невалидной (умерла/спряталась/стала другом)
            if (_currentTarget == null || !IsValidTarget(_currentTarget))
            {
                FindNearestTarget();
            }

            // Если цель найдена
            if (_currentTarget != null)
            {
                RotateTowardsTarget();

                if (Time.time >= _nextFireTime)
                {
                    Fire();
                    _nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }

        private void FindNearestTarget()
        {
            // 1. Берем ВСЕ коллайдеры вокруг (без фильтра слоев)
            Collider[] hits = Physics.OverlapSphere(transform.position, attackRange);

            float closestDist = Mathf.Infinity;
            Transform bestTarget = null;

            foreach (var hit in hits)
            {
                Transform targetTrans = hit.transform;

                // Оптимизация: Сразу пропускаем себя и детей
                if (targetTrans.root == transform.root) continue;

                // Проверяем, подходит ли цель
                if (IsValidTarget(targetTrans))
                {
                    float dist = Vector3.Distance(transform.position, targetTrans.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        bestTarget = targetTrans;
                    }
                }
            }

            _currentTarget = bestTarget;
        }

        private bool IsValidTarget(Transform target)
        {
            if (target == null) return false;

            // 1. Пропускаем само растение (на всякий случай)
            if (target == transform || target.IsChildOf(transform)) return false;

            // 2. Ищем Health (Самая важная проверка: если нет ХП, это не враг)
            // Используем GetComponentInParent, так как коллайдер может быть на части тела
            Health health = target.GetComponentInParent<Health>();
            if (health == null) return false;

            // 3. Жив ли?
            if (health.GetHealth() <= 0) return false;

            // 4. ПРОВЕРКА "СВОЙ-ЧУЖОЙ"
            // Получаем сетевой объект цели, чтобы узнать владельца
            NetworkObject targetNO = target.GetComponentInParent<NetworkObject>();

            if (targetNO != null)
            {
                // ID того, кто поставил эту турель
                int myOwnerId = _parentPlant.OwnerActorNumber.Value;

                // ID цели
                int targetOwnerId = targetNO.OwnerId;

                // Если ID совпадают — это СОЮЗНИК, не стреляем
                if (targetOwnerId == myOwnerId)
                    return false;

                // Если ID = -1 (Сервер/Нейтрал) и мой ID = -1 (Сервер поставил турель) — тоже не стреляем
                // (Опционально, если хотите, чтобы нейтральные турели не били нейтральных мобов)
            }

            // 5. Проверка дистанции (если цель убежала за радиус + небольшой запас)
            if (Vector3.Distance(transform.position, target.position) > attackRange + 1.0f)
                return false;

            return true;
        }

        private void RotateTowardsTarget()
        {
            if (turretHead == null) return;

            Vector3 dir = _currentTarget.position - turretHead.position;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion rot = Quaternion.LookRotation(dir);
                turretHead.rotation = Quaternion.Slerp(turretHead.rotation, rot, Time.deltaTime * 10f);
            }
        }

        private void Fire()
        {
            if (projectilePrefab == null) return;

            // Создаем пулю
            GameObject proj = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);

            // Регистрируем в сети
            Spawn(proj);

            // Инициализируем (передаем урон и ID владельца, чтобы пуля не дамажила своих при взрыве/попадании)
            if (proj.TryGetComponent(out PlantProjectile bulletScript))
            {
                bulletScript.Initialize(damage, _parentPlant.OwnerActorNumber.Value);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}