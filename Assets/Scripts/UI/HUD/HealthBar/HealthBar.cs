using UnityEngine;
using UnityEngine.UI;

namespace UI.HUD.HealthBar
{
    public class HealthBar : MonoBehaviour
    {
        public Slider slider;

        public void SetHealth(float health)
        {
            if (slider != null)
                slider.value = health;
        }

        public void SetMaxHealth(float maxHealth)
        {
            if (slider != null)
                slider.maxValue = maxHealth;
        }
    }
}
