namespace Intelibill.Domain.ValueObjects;

public static class TillMoney
{
    public const decimal Maximum = 9999999999999999.99m;
    public static bool IsValid(decimal amount) => amount >= -Maximum && amount <= Maximum && decimal.Round(amount, 2) == amount;
}
