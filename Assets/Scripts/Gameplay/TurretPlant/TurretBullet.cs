using Core.Components;
using FishNet.Object;
using UnityEngine;

namespace Gameplay.TurretPlant
{
    [RequireComponent(typeof(Rigidbody))]
    public class TurretBullet : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int damage = 15;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private GameObject impactEffect;

        private Transform _turretOwner;
        private int _turretOwnerId = -1;

        public void SetOwner(Transform turret, int ownerId)
        {
            _turretOwner = turret;
            _turretOwnerId = ownerId;
        }

        public void SetDamage(int newDamage)
        {
            damage = newDamage;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (IsServerInitialized)
            {
                Invoke(nameof(DespawnBullet), lifetime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;

            // Не бьём саму турель
            if (_turretOwner != null && other.transform == _turretOwner) return;

            // Не бьём владельца турели
            var netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerId == _turretOwnerId) return;

            // Не бьём нейтралов (турель атакует только игроков)
            if (other.GetComponent<AI.Neutral.Neutral>() != null) return;

            // Не бьём другие турели того же владельца
            var turret = other.GetComponent<TurretPlant>();
            if (turret != null && turret.PlantedOwnerId == _turretOwnerId) return;

            if (other.TryGetComponent(out Health hp))
            {
                hp.TakeDamage(damage, _turretOwner);
                Debug.Log($"[TurretBullet] Hit {other.name} for {damage} damage");
            }

            DespawnBullet();
        }

        [Server]
        private void DespawnBullet()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                if (impactEffect != null)
                {
                    GameObject effect = Instantiate(impactEffect, transform.position, transform.rotation);
                    Destroy(effect, 2f);
                }
                NetworkObject.Despawn();
            }
        }
    }
}
