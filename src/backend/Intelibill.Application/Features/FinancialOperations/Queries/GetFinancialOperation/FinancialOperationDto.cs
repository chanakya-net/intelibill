using System.Text.Json;

namespace Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;

/// <param name="Response">The body the first commit answered with, exactly as a retry replays it.</param>
public sealed record FinancialOperationDto(
    string OperationType,
    string OperationKey,
    Guid TargetId,
    Guid ActorUserId,
    DateTimeOffset CompletedAt,
    JsonElement Response);
