using Core.Components;
using FishNet.Object; // Для доступа к NetworkObject
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using FischlWorks_FogWar; // Не забудьте этот namespace

namespace UI.Health
{
    public class HealthBarController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image healthFillImage;
        [SerializeField] private Image damageOverlayImage;
        [SerializeField] private Image borderImage;
        [SerializeField] private Image backgroundImage;

        [Header("Settings")]
        [SerializeField] private float damageOverlayDelay = 0.5f;
        [SerializeField] private float damageOverlaySpeed = 2f;
        [SerializeField] private Color friendlyBorderColor = Color.yellow;
        [SerializeField] private Color enemyBorderColor = Color.black;
        [SerializeField] private string playerTag = "Player";

        [Header("Transparency Settings")]
        [SerializeField][Range(0f, 1f)] private float healthBarAlpha = 0.7f;

        [Header("Positioning")]
        [SerializeField] private float verticalOffset = 1.5f;
        [SerializeField] private float zOffset = 0f;

        private Core.Components.Health targetHealth;
        private Camera mainCamera;
        private Coroutine damageOverlayCoroutine;

        // Поля для тумана
        private csFogVisibilityAgent csFogVisibilityAgent;

        public void Initialize(Core.Components.Health healthComponent)
        {
            targetHealth = healthComponent;
            mainCamera = Camera.main;

            // Попытка найти компонент тумана на цели
            if (targetHealth != null)
                csFogVisibilityAgent = targetHealth.GetComponent<csFogVisibilityAgent>();

            // --- ПРОВЕРКА FishNet: Цвет рамки для МОЕГО ИГРОКА ---
            bool isLocalPlayerCharacter = false;

            if (targetHealth != null)
            {
                // Если Health это NetworkBehaviour, можно проверить IsOwner
                // Или проверяем через NetworkObject
                var no = targetHealth.GetComponent<NetworkObject>();
                if (no != null && no.IsOwner && targetHealth.CompareTag(playerTag))
                {
                    isLocalPlayerCharacter = true;
                }
            }

            if (isLocalPlayerCharacter)
            {
                if (borderImage != null) borderImage.color = friendlyBorderColor;
            }
            else
            {
                if (borderImage != null) borderImage.color = enemyBorderColor;
            }

            // Применение прозрачности
            SetAlpha(healthBarAlpha);

            // Инициализируем полоску
            UpdateHealthBar(targetHealth.GetHealth(), targetHealth.MaxHealth);
        }

        private void OnDestroy()
        {
            // Очистка не требуется
        }

        private void Update()
        {
            if (targetHealth == null || mainCamera == null) return;

            // Позиция над объектом
            Vector3 targetPosition = targetHealth.transform.position + Vector3.up * verticalOffset;
            transform.position = targetPosition;

            // --- Вращение только по X ---
            Vector3 cameraEuler = mainCamera.transform.rotation.eulerAngles;
            transform.rotation = Quaternion.Euler(cameraEuler.x, 0f, 0f);

            // --- Смещение по Z ---
            transform.position += transform.forward * zOffset;
        }

        // Логика Тумана Войны
        private void FixedUpdate()
        {
            // Если цель мертва или ссылок нет, выходим
            if (targetHealth == null) return;

            //  ПОЧЕМУ ОНО НЕ РАБОТАЕТ ОООАОАОААООА
            //// Если здоровье <= 0, бар скрыт всегда
            //if (targetHealth.GetHealth() <= 0)
            //{
            //    SetVisibility(false);
            //    return;
            //}

            //// Если есть агент тумана, проверяем видимость
            //if (csFogVisibilityAgent != null)
            //{
            //    bool isVisibleInFog = csFogVisibilityAgent.GetVisibility();
            //    SetVisibility(isVisibleInFog);
            //}
            //else
            //{
            //    // Если тумана нет, бар всегда виден (пока живо существо)
            //    SetVisibility(true);
            //}
        }

        private void SetVisibility(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        // Этот метод вызывается из Health.OnHealthChanged
        public void UpdateHealthBar(float currentHealth, float maxHealth)
        {
            float healthRatio = 0f;
            if (maxHealth > 0)
                healthRatio = currentHealth / maxHealth;

            if (healthFillImage != null)
                healthFillImage.fillAmount = healthRatio;

            if (damageOverlayImage != null)
            {
                // === ИСПРАВЛЕНИЕ ОШИБКИ ===
                // Мы не можем запустить Coroutine, если объект выключен (например, скрыт туманом).
                if (gameObject.activeInHierarchy)
                {
                    // Если объект активен - запускаем красивую анимацию
                    if (damageOverlayCoroutine != null)
                    {
                        StopCoroutine(damageOverlayCoroutine);
                    }
                    damageOverlayCoroutine = StartCoroutine(AnimateDamageOverlay(healthRatio));
                }
                else
                {
                    // Если объект выключен - просто меняем значение мгновенно,
                    // чтобы когда он стал видимым, полоска была правильной.
                    damageOverlayImage.fillAmount = healthRatio;
                }
            }
        }

        private IEnumerator AnimateDamageOverlay(float targetFillAmount)
        {
            if (damageOverlayImage != null && healthFillImage != null)
            {
                damageOverlayImage.fillAmount = Mathf.Max(damageOverlayImage.fillAmount, healthFillImage.fillAmount);

                yield return new WaitForSeconds(damageOverlayDelay);

                while (damageOverlayImage.fillAmount > targetFillAmount)
                {
                    damageOverlayImage.fillAmount -= Time.deltaTime * damageOverlaySpeed;
                    if (damageOverlayImage.fillAmount < targetFillAmount)
                    {
                        damageOverlayImage.fillAmount = targetFillAmount;
                    }
                    yield return null;
                }
            }
        }

        private void SetAlpha(float alpha)
        {
            SetImageAlpha(healthFillImage, alpha);
            SetImageAlpha(damageOverlayImage, alpha);
            SetImageAlpha(borderImage, alpha);
            SetImageAlpha(backgroundImage, alpha);
        }

        private void SetImageAlpha(Image img, float alpha)
        {
            if (img != null)
            {
                Color color = img.color;
                color.a = alpha;
                img.color = color;
            }
        }
    }
}