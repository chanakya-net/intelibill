using ErrorOr;
using InventoryAI.Application.Common.Models;
using InventoryAI.Domain.Enums;

namespace InventoryAI.Application.Common.Interfaces;

public interface IExternalAuthProvider
{
    ExternalAuthProvider Provider { get; }
    Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
