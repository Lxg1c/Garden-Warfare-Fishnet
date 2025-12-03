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
        [SerializeField] private float speed = 20f;
        [SerializeField] private GameObject impactEffect;

        private Transform _owner;
        private Rigidbody _rb;
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
            _rb = GetComponent<Rigidbody>();
            
            if (IsServerInitialized)
            {
                Invoke(nameof(DespawnBullet), lifetime);
            }
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized) return;

            _rb.MovePosition(transform.position + transform.forward * speed * Time.fixedDeltaTime);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;
            
            if (_owner != null && other.transform == _owner) return;

            if (other.TryGetComponent(out Health hp))
            {
                hp.TakeDamage(damage, _owner, _ownerNetworkObject);
            }
            else
            {
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