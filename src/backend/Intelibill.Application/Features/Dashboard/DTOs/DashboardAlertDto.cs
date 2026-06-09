namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardAlertDto(
    string AlertType,
    int Priority,
    string Title,
    string Message,
    string ActionLabel,
    string ActionRoute);
