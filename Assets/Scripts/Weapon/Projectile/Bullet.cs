using Core.Components;
using UnityEngine;
using FishNet.Object;

namespace Weapon.Projectile
{
    [RequireComponent(typeof(Rigidbody))]
    public class Bullet : NetworkBehaviour
    {
        [Header("Bullet Settings")]
        [SerializeField] private int damage = 10;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private GameObject impactEffect;

        private Transform _owner;
        private NetworkObject _ownerNetworkObject;

        public void SetOwner(Transform owner)
        {
            _owner = owner;
            _ownerNetworkObject = owner != null ? owner.GetComponent<NetworkObject>() : null;
        }

        public void SetDamage(float newDamage)
        {
            damage = Mathf.RoundToInt(newDamage);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (IsServerInitialized)
            {
                Invoke(nameof(DespawnBullet), lifetime);
            }
        }

        // Движение пули обрабатывается физикой через AddForce в WeaponController.SpawnBullet()
        // Не используем FixedUpdate + MovePosition чтобы избежать двойного движения

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;

            if (_owner != null && other.transform == _owner) return;

            // Ищем Health на самом объекте ИЛИ на родителях (для случая когда коллайдер на дочернем объекте)
            Health hp = other.GetComponent<Health>();
            if (hp == null)
            {
                hp = other.GetComponentInParent<Health>();
            }

            if (hp != null)
            {
                Debug.Log($"Пытаемся нанести урон объекту {hp.name}");
                hp.TakeDamage(damage, _owner, _ownerNetworkObject);
            }
            else
            {
                Debug.Log($"Нет компонента здоровья на {other.name}");
                SpawnImpactEffect();
            }

            DespawnBullet();
        }

        [Server]
        private void SpawnImpactEffect()
        {
            if (impactEffect != null)
            {
                GameObject effect = Instantiate(impactEffect, transform.position, transform.rotation);
                NetworkObject netObj = effect.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    Spawn(netObj);
                }
                Destroy(effect, 2f);
            }
        }

        [Server]
        private void DespawnBullet()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                SpawnImpactEffect();
                
                NetworkObject.Despawn();
            }
        }
    }
}