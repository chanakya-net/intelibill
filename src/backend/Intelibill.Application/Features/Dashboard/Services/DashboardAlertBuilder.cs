using Intelibill.Application.Features.Dashboard.DTOs;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class DashboardAlertBuilder
{
    internal static List<DashboardAlertDto> BuildAlerts(
        bool isStaff,
        int criticalStockCount,
        int runningLowStockCount,
        CustomerDueDto? highestDueCustomer,
        decimal creditSalesPercentage)
    {
        var alerts = new List<DashboardAlertDto>();
        if (criticalStockCount > 0)
            alerts.Add(new DashboardAlertDto("CriticalStock", 1));
        if (!isStaff && highestDueCustomer is not null)
            alerts.Add(new DashboardAlertDto("HighestDue", 2));
        if (runningLowStockCount > 0)
            alerts.Add(new DashboardAlertDto("RunningLowStock", 3));
        if (!isStaff && creditSalesPercentage >= SalesKpiCalculator.CreditShareWarningThreshold)
            alerts.Add(new DashboardAlertDto("CreditShareWarning", 4));
        return alerts;
    }
}
