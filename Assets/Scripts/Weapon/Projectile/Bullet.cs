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
            _rb = GetComponent<Rigidbody>();
            Invoke(nameof(DespawnBullet), lifetime);
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
                hp.TakeDamageServerRpc(damage, NetworkObject);

            DespawnBullet();
        }

        [Server]
        private void DespawnBullet()
        {
            if (NetworkObject != null)
                NetworkObject.Despawn();
        }
    }
}