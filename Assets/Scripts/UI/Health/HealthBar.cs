using UnityEngine;
using UnityEngine.UI;

namespace UI.Health
{
    public class HealthBar : MonoBehaviour
    {
        public Slider slider;

        public void SetHealth(float health)
        {
            Debug.Log($"Ставим slider value {health}");
            slider.value = health;
        }

        public void SetMaxHealth(float maxHealth)
        {
            slider.maxValue = maxHealth;
        }
    }
}