namespace Intelibill.Domain.ReadModels;

public sealed record PartyDirectoryQuery(string? Search, string Status = "all", int PageNumber = 1, int PageSize = 20);
public sealed record PartyDirectoryPage<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize);
public sealed record CustomerDirectorySummary(int TotalCustomers, decimal OutstandingBalance, int OutstandingCount,
    decimal TotalCreditIssued, int AccountsWithCredit, decimal MonthlyRevenue);
public sealed record SupplierDirectorySummary(int TotalSuppliers, int ActiveSuppliers, int PreferredSuppliers, decimal PendingPayables);
