using FishNet.Object;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UI.HUD.HealthBar
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
        [SerializeField] private float zOffset;

        private Core.Components.Health _targetHealth;
        private Camera _mainCamera;
        private Coroutine _damageOverlayCoroutine;

        public void Initialize(Core.Components.Health healthComponent)
        {
            if (healthComponent == null)
            {
                Debug.LogError("Health component is null in HealthBarController!");
                return;
            }

            _targetHealth = healthComponent;
            _mainCamera = Camera.main;
            
            bool isLocalPlayerCharacter = false;
            var networkObject = _targetHealth.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsOwner && _targetHealth.CompareTag(playerTag))
            {
                isLocalPlayerCharacter = true;
            }
            
            if (borderImage != null)
            {
                borderImage.color = isLocalPlayerCharacter ? friendlyBorderColor : enemyBorderColor;
            }

            SetAlpha(healthBarAlpha);
            
            UpdateHealthBar(_targetHealth.GetHealth(), _targetHealth.MaxHealth);
        }

        private void Update()
        {
            if (_targetHealth == null || _mainCamera == null) 
            {
                if (_targetHealth == null)
                {
                    Destroy(gameObject);
                }
                return;
            }

            Vector3 targetPosition = _targetHealth.transform.position + Vector3.up * verticalOffset;
            transform.position = targetPosition;
            
            Vector3 cameraEuler = _mainCamera.transform.rotation.eulerAngles;
            transform.rotation = Quaternion.Euler(cameraEuler.x, 0f, 0f);
            
            transform.position += transform.forward * zOffset;
        }

        public void UpdateHealthBar(float currentHealth, float maxHealth)
        {
            if (healthFillImage == null) return;

            float healthRatio = maxHealth > 0 ? currentHealth / maxHealth : 0f;
            healthFillImage.fillAmount = healthRatio;
            
            if (damageOverlayImage != null && gameObject.activeInHierarchy)
            {
                if (_damageOverlayCoroutine != null)
                {
                    StopCoroutine(_damageOverlayCoroutine);
                }
                _damageOverlayCoroutine = StartCoroutine(AnimateDamageOverlay(healthRatio));
            }
            else if (damageOverlayImage != null)
            {
                damageOverlayImage.fillAmount = healthRatio;
            }
        }

        private IEnumerator AnimateDamageOverlay(float targetFillAmount)
        {
            if (damageOverlayImage == null || healthFillImage == null) yield break;
            
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

        private void OnDestroy()
        {
            if (_damageOverlayCoroutine != null)
            {
                StopCoroutine(_damageOverlayCoroutine);
            }
        }
    }
}