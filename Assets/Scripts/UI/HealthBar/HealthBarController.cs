using FischlWorks_FogWar;
using FishNet.Object;
using UnityEngine;

namespace UI.HealthBar
{
    public class HealthBarController : NetworkBehaviour
    {
        [SerializeField] private HealthBar healthBar;
        [SerializeField] private Core.Components.Health health;
        
        [Header("Optional - for owner checks")]
        [SerializeField] private bool checkOwnerDamage;
        [SerializeField] private csFogVisibilityAgent csFogVisibilityAgent;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            
            if (health == null)
                health = GetComponent<Core.Components.Health>();
                
            if (health != null)
            {
                // Подписываемся на события Health
                health.onHealthChanged.AddListener(OnHealthChanged);
                health.onDeath.AddListener(OnDeath);
                
                // Инициализируем HealthBar
                if (healthBar != null)
                {
                    healthBar.SetMaxHealth(health.MaxHealth);
                    healthBar.SetHealth(health.CurrentHealth);
                    healthBar.gameObject.SetActive(true);
                }
            }
        }

        private void LateUpdate()
        {
            // Billboarding - HealthBar всегда смотрит на камеру
            if (Camera.main != null && healthBar != null)
            {
                healthBar.transform.rotation = Camera.main.transform.rotation;
            }
        }

        private void FixedUpdate()
        {
            // Управление видимостью через Fog of War
            if (csFogVisibilityAgent != null && healthBar != null)
            {
                bool visible = csFogVisibilityAgent.GetVisibility();
                healthBar.gameObject.SetActive(visible);
            }
        }

        private void OnHealthChanged(float newHealth)
        {
            if (healthBar != null)
            {
                healthBar.SetHealth(newHealth);
                
                // Показываем HealthBar при изменении здоровья
                if (!healthBar.gameObject.activeSelf)
                    healthBar.gameObject.SetActive(true);
            }
        }

        private void OnDeath()
        {
            // Скрываем HealthBar при смерти
            if (healthBar != null)
            {
                healthBar.SetHealth(0);
                healthBar.gameObject.SetActive(false);
            }
        }

        // Метод для респавна
        [ObserversRpc]
        public void OnRespawn()
        {
            if (healthBar != null && health != null)
            {
                healthBar.SetMaxHealth(health.MaxHealth);
                healthBar.SetHealth(health.CurrentHealth);
                healthBar.gameObject.SetActive(true);
            }
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            
            // Отписываемся от событий
            if (health != null)
            {
                health.onHealthChanged.RemoveListener(OnHealthChanged);
                health.onDeath.RemoveListener(OnDeath);
            }
        }
    }
}