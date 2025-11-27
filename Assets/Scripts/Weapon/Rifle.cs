using UnityEngine;
using Weapon.Base;

namespace Weapon.Types
{
    public class Rifle : RangedWeapon
    {
        [Header("Rifle Settings")]
        public GameObject bulletPrefab;
        public Transform shootPoint;
        public float bulletForce = 20f;

        // Use() наследуется от RangedWeapon (просто сброс таймера)

        public override void UseLocal()
        {
            // Визуальные эффекты выстрела
            // AudioSource.PlayOneShot(shootSound);
            // Debug.Log($"[Client] Bang! {weaponName} fired.");
        }
    }
}