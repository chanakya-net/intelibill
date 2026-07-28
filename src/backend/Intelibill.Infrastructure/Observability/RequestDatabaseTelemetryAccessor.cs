namespace Intelibill.Infrastructure.Observability;

public sealed class RequestDatabaseTelemetryAccessor
{
    private readonly AsyncLocal<RequestState?> _current = new();

    public RequestDatabaseTelemetry? Current => _current.Value?.Telemetry;

    public RequestScope BeginRequest()
    {
        var previous = _current.Value;
        var current = new RequestState(new RequestDatabaseTelemetry());
        _current.Value = current;
        return new RequestScope(this, current, previous);
    }

    private void EndRequest(RequestState current, RequestState? previous)
    {
        if (ReferenceEquals(_current.Value, current))
        {
            _current.Value = previous;
        }
    }

    internal sealed record RequestState(RequestDatabaseTelemetry Telemetry);

    public sealed class RequestScope : IDisposable
    {
        private readonly RequestDatabaseTelemetryAccessor _owner;
        private readonly RequestState _current;
        private readonly RequestState? _previous;
        private int _disposed;

        internal RequestScope(
            RequestDatabaseTelemetryAccessor owner,
            RequestState current,
            RequestState? previous)
        {
            _owner = owner;
            _current = current;
            _previous = previous;
        }

        public RequestDatabaseTelemetry Telemetry => _current.Telemetry;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.EndRequest(_current, _previous);
            }
        }
    }
}
