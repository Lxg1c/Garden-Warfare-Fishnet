using FishNet.Object;
using UnityEngine;
using Weapon;
using Weapon.Interfaces;
using Weapon.Types;

public class WeaponController : NetworkBehaviour
{
    public Weapon.Base.WeaponBase currentWeapon;
    public bool holdToFire = true;

    private PlayerInputActions _input;
    private bool _enabled = true;

    private void Awake()
    {
        _input = new PlayerInputActions();
    }

    private void OnEnable() => _input.Enable();
    private void OnDisable() => _input.Disable();

    private void Update()
    {
        if (!IsOwner || !_enabled || currentWeapon == null)
            return;

        bool trigger = holdToFire
            ? _input.Player.Fire.IsPressed()
            : _input.Player.Fire.WasPressedThisFrame();

        if (trigger && currentWeapon.CanUse())
        {
            UseWeaponServerRpc();
        }

        if (_input.Player.Reload.WasPressedThisFrame() && currentWeapon is IReloadable)
        {
            ReloadServerRpc();
        }
    }

    /* =========================
     *     SERVER RPCs
     * ========================= */

    [ServerRpc]
    private void UseWeaponServerRpc()
    {
        if (currentWeapon == null || !currentWeapon.CanUse())
            return;

        // Сервер создаёт пулю
        SpawnBullet();

        // Все клиенты проигрывают UseLocal
        UseWeaponObserversRpc();
    }

    [ObserversRpc]
    private void UseWeaponObserversRpc()
    {
        currentWeapon.UseLocal();
    }

    [ServerRpc]
    private void ReloadServerRpc()
    {
        if (currentWeapon is IReloadable)
        {
            ReloadObserversRpc();
        }
    }


    [ObserversRpc]
    private void ReloadObserversRpc()
    {
        if (currentWeapon is RangedWeapon rw)
            rw.ReloadLocal();
    }

    /* =========================
     *     BULLET SPAWN
     * ========================= */
    
    private void SpawnBullet()
    {
        Rifle rifle = currentWeapon as Rifle;
        if (rifle == null) return;

        GameObject bullet = Instantiate(
            rifle.bulletPrefab,
            rifle.shootPoint.position,
            rifle.shootPoint.rotation
        );

        // FishNet spawn
        Spawn(bullet);

        if (bullet.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.AddForce(rifle.shootPoint.forward * rifle.bulletForce, ForceMode.Impulse);
        }
    }

    public void SetWeaponEnabled(bool isEnabled)
    {
        _enabled = isEnabled;
    }
}
