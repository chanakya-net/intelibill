using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Only migration fixtures may represent a writer from before confirmation issuance shipped.</summary>
internal sealed class P0311LegacyIssuance
{
    internal bool BeforeConfirmations { get; set; } = true;
    internal void Install(IServiceCollection services) => services.AddScoped<IPaymentConfirmationStore>(p =>
        new LegacyStore(this, (IPaymentConfirmationStore)p.GetRequiredService<IPaymentStore>()));
    private sealed class LegacyStore(P0311LegacyIssuance fixture, IPaymentConfirmationStore current) : IPaymentConfirmationStore
    {
        public Task IssueAsync(Guid account, Guid order, Guid? attempt, Guid? receipt, DateTimeOffset issuedAt, CancellationToken ct) =>
            fixture.BeforeConfirmations ? Task.CompletedTask : current.IssueAsync(account, order, attempt, receipt, issuedAt, ct);
        public Task<PaymentConfirmationV1[]> ListConfirmationsAsync(Guid account, Guid order, Guid? after, CancellationToken ct) => current.ListConfirmationsAsync(account, order, after, ct);
        public Task<PaymentConfirmationSnapshotV1?> FindConfirmationAsync(Guid account, Guid order, Guid id, CancellationToken ct) => current.FindConfirmationAsync(account, order, id, ct);
    }
}
