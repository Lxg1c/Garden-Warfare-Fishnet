using UnityEngine;

namespace AI.Plant
{
    public class PlantTurret : MonoBehaviour
    {
        public bool active;
        public float attackRange = 7f;
        public float shootInterval = 1f;
        public GameObject projectilePrefab;
        public Transform shootPoint;

        private float _timer;

        public void Activate()
        {
            active = true;
        }

        private void Update()
        {
            if (!active) return;

            _timer -= Time.deltaTime;
            if (_timer > 0) return;

            GameObject target = FindNearestEnemy();
            if (target == null) return;

            Shoot(target.transform);
            _timer = shootInterval;
        }

        GameObject FindNearestEnemy()
        {
            // позже сделаем нормальный EnemyManager
            Collider[] hits = Physics.OverlapSphere(transform.position, attackRange);

            foreach (var h in hits)
            {
                if (h.CompareTag("Player"))
                    return h.gameObject;
            }
            return null;
        }

        void Shoot(Transform target)
        {
            var p = Instantiate(projectilePrefab, shootPoint.position, Quaternion.LookRotation(target.position - shootPoint.position));
        }
    }
}

