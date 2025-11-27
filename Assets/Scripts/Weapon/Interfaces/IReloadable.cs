namespace Weapon.Interfaces
{
    public interface IReloadable
    {
        // Логика сервера (восстановить число int ammo)
        void Reload();

        // Логика клиента (проиграть звук/анимацию)
        void ReloadLocal();
    }
}