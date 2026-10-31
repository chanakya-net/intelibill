namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The SMS transport seam. It takes a rendered body rather than a code and a template id: the
/// outbox has already rendered and encrypted the message, the DLT template id is per-deployment
/// configuration rather than per-call data, and keeping the code out of this signature means no
/// transport implementation is ever in a position to log one.
/// </summary>
public interface ISmsSender
{
    Task SendIdentityAsync(string destination, string body, CancellationToken ct);
}
