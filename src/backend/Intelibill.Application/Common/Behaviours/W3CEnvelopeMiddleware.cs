using System.Diagnostics;
using Wolverine;

namespace Intelibill.Application.Common.Behaviours;

/// <summary>
/// Outgoing side. Registered via opts.OutgoingRules.Add(new W3COutboundEnvelopeModifier()).
/// Copies the active W3C traceparent/tracestate into the Wolverine envelope headers
/// so downstream consumers can continue the trace.
/// </summary>
public sealed class W3COutboundEnvelopeModifier : IEnvelopeRule
{
    public void Modify(Envelope envelope)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        var flags = activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00";
        var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{flags}";
        envelope.Headers["traceparent"] = traceparent;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
            envelope.Headers["tracestate"] = activity.TraceStateString;
    }
}

/// <summary>
/// Incoming side. Wolverine conventional middleware registered via
/// opts.Policies.AddMiddleware&lt;W3CInboundEnvelopeMiddleware&gt;().
/// Reads traceparent/tracestate from envelope headers and starts a child Activity
/// so the consumer span is parented to the publisher trace.
/// </summary>
public sealed class W3CInboundEnvelopeMiddleware
{
    private static readonly ActivitySource Source = new("InteliBill.Wolverine", "1.0.0");

    /// <summary>
    /// Wolverine injects the <see cref="Envelope"/> automatically.
    /// Returning <see cref="Activity?"/> makes it available to <see cref="After"/>.
    /// </summary>
    public static (HandlerContinuation, Activity?) Before(Envelope envelope)
    {
        if (!envelope.Headers.TryGetValue("traceparent", out var traceparent) || traceparent is null)
            return (HandlerContinuation.Continue, null);

        envelope.Headers.TryGetValue("tracestate", out var tracestate);

        if (!ActivityContext.TryParse(traceparent, tracestate, isRemote: true, out var parentContext))
            return (HandlerContinuation.Continue, null);

        var activity = Source.StartActivity(
            $"wolverine.consume {envelope.MessageType ?? "unknown"}",
            ActivityKind.Consumer,
            parentContext);

        return (HandlerContinuation.Continue, activity);
    }

    public static void After(Activity? activity) => activity?.Stop();
}
