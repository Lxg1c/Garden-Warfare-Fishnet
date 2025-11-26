using FishNet.Object;
using UnityEngine;
using UnityEngine.Events;

namespace Core.Components
{
    public class Health : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = true;

        [Header("Events")]
        public UnityEvent<float, NetworkObject> onDamageTakenBy;
        public UnityEvent onDamageTaken;
        public UnityEvent onHealed;
        public UnityEvent onDeath;
        public UnityEvent<float> onHealthChanged;

        private float _currentHealth;
        private bool _isDead;

        public float CurrentHealth => _currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsDead => _isDead;
        public float HealthPercentage => _currentHealth / maxHealth;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (IsServerInitialized)
            {
                InitializeHealth();
            }
        }

        [Server]
        private void InitializeHealth()
        {
            _currentHealth = maxHealth;
            _isDead = false;
        }

        // ------------ Public RPC API ----------------

        [ServerRpc(RequireOwnership = false)]
        public void TakeDamageServerRpc(float damage, NetworkObject attacker = null)
        {
            if (_isDead || damage <= 0) return;
            TakeDamageInternal(damage, attacker);
        }

        [ServerRpc(RequireOwnership = false)]
        public void HealServerRpc(float healAmount)
        {
            if (_isDead || healAmount <= 0) return;
            HealInternal(healAmount);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetHealthServerRpc(float newHealth)
        {
            newHealth = Mathf.Clamp(newHealth, 0, maxHealth);
            _currentHealth = newHealth;
            HealthChangedObserversRpc(_currentHealth);

            if (_currentHealth <= 0 && !_isDead)
                Die();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RestoreHealthServerRpc()
        {
            _currentHealth = maxHealth;
            _isDead = false;
            HealthChangedObserversRpc(_currentHealth);
        }

        [ServerRpc(RequireOwnership = false)]
        public void KillServerRpc()
        {
            if (_isDead) return;
            TakeDamageInternal(_currentHealth, null);
        }

        // ------------ Internal Logic ------------

        [Server]
        private void TakeDamageInternal(float damage, NetworkObject attacker)
        {
            _currentHealth = Mathf.Max(0, _currentHealth - damage);

            DamageTakenObserversRpc(damage, attacker);

            if (_currentHealth <= 0 && !_isDead)
                Die();
        }

        [Server]
        private void HealInternal(float healAmount)
        {
            _currentHealth = Mathf.Min(maxHealth, _currentHealth + healAmount);
            HealedObserversRpc();
        }

        [Server]
        private void Die()
        {
            _isDead = true;
            DeathObserversRpc();

            if (destroyOnDeath)
                Invoke(nameof(DestroyObject), 2f);
        }

        [Server]
        private void DestroyObject()
        {
            if (NetworkObject != null)
                NetworkObject.Despawn();
        }

        // -------- Observers RPC Events --------

        [ObserversRpc]
        private void DamageTakenObserversRpc(float damage, NetworkObject attacker)
        {
            onDamageTaken?.Invoke();
            onDamageTakenBy?.Invoke(damage, attacker);
            onHealthChanged?.Invoke(_currentHealth);
        }

        [ObserversRpc]
        private void HealedObserversRpc()
        {
            onHealed?.Invoke();
            onHealthChanged?.Invoke(_currentHealth);
        }

        [ObserversRpc]
        private void DeathObserversRpc()
        {
            onDeath?.Invoke();
            onHealthChanged?.Invoke(0);
        }

        [ObserversRpc]
        private void HealthChangedObserversRpc(float newHealth)
        {
            onHealthChanged?.Invoke(newHealth);
        }

        // --------- Utility Methods ---------

        public bool IsAlive() => !_isDead && _currentHealth > 0;
        public float GetHealthPercentage() => HealthPercentage;

        [Server]
        public void SetMaxHealth(float newMaxHealth)
        {
            maxHealth = newMaxHealth;
            _currentHealth = Mathf.Min(_currentHealth, maxHealth);
            HealthChangedObserversRpc(_currentHealth);
        }

        [Server]
        public void Respawn()
        {
            _currentHealth = maxHealth;
            _isDead = false;
            HealedObserversRpc();
        }
    }
}