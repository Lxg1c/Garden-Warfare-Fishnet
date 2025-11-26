namespace Weapon.Interfaces
{
    /// <summary>
    /// Интерфейс для оружия, которое может перезаряжаться.
    /// </summary>
    public interface IReloadable
    {
        void Reload();
        bool CanReload();
    }
}