using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Development and Testing only, and the options validator refuses it anywhere else. It is the SMS
/// equivalent of a local mail catcher: nothing leaves the machine, and the rendered message is
/// written to the log so a developer can read the code. That is the one place a code is ever
/// logged, which is why the environment restriction is enforced at startup rather than here.
/// </summary>
internal sealed partial class LocalCaptureSmsSender(ILogger<LocalCaptureSmsSender> logger) : ISmsSender
{
    public Task SendIdentityAsync(string destination, string body, CancellationToken ct)
    {
        if (!logger.IsEnabled(LogLevel.Information))
            return Task.CompletedTask;

        var maskedDestination = PhoneNumberFormat.Mask(destination);
        LogCapturedMessage(logger, maskedDestination, body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local SMS capture for {MaskedDestination}: {Body}")]
    private static partial void LogCapturedMessage(ILogger logger, string maskedDestination, string body);
}

/// <summary>
/// What runs when phone authentication is on but no transport is configured. It fails rather than
/// silently dropping a code, so the queue's attempt counts show that delivery is broken.
/// </summary>
internal sealed class UnconfiguredSmsSender : ISmsSender
{
    public Task SendIdentityAsync(string destination, string body, CancellationToken ct) =>
        throw new InvalidOperationException("No SMS transport is configured. Set PhoneAuth:Provider and its credentials, or PhoneAuth:LocalCapture in development.");
}
