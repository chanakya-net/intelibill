namespace Intelibill.Licensing.Application.Payments;

internal static class PaymentGatewayCall
{
    internal static async Task<GatewayResult<T>> RunAsync<T>(Func<CancellationToken, Task<GatewayResult<T>>> call,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return await call(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return GatewayResult.Unavailable<T>();
        }
        catch (Exception error) when (error is HttpRequestException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return GatewayResult.Unavailable<T>();
        }
    }
}
