namespace Intelibill.Application.Features.Tills;

public sealed record TillPostingContext(Guid SessionId, string? OverrideReason = null);
