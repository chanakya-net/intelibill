namespace Intelibill.Application.Common.Exceptions;

public sealed class InventoryUpdateConflictException : Exception
{
    public const string ErrorCode = "Inventory.UpdateConflict";

    public InventoryUpdateConflictException(string message) : base(message) { }
}
