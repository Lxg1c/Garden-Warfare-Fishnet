using UnityEngine;

namespace Weapon.Base
{
    public abstract class WeaponBase : MonoBehaviour
    {
        [Header("Base Weapon Settings")]
        public string weaponName = "Weapon";
        public int damage = 10;
        public float useRate = 0.5f;

        private float _nextUseTime;

        public virtual bool CanUse() => Time.time >= _nextUseTime;

        protected void MarkUse() => _nextUseTime = Time.time + useRate;

        /// <summary>
        /// Локальная логика использования (не сеть!)
        /// </summary>
        public abstract void UseLocal();
    }
}