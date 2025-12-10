namespace Gameplay.TurretPlant
{
    public enum TurretPlantState
    {
        Wild,       // Дикое - стоит в мире, атакует если атаковано
        Carried,    // Несётся игроком
        Planted     // Посажено - турель, атакует врагов владельца
    }
}
