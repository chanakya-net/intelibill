namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record CustomerDueDto(Guid CustomerId, string DisplayName, decimal OutstandingDue);
