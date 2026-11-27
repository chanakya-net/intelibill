using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class PhoneAuthPolicy(IOptions<PhoneAuthOptions> options) : IPhoneAuthPolicy
{
    public bool Enabled => options.Value.Enabled;

    public int CodeLength => options.Value.CodeLength;

    public TimeSpan CodeLifetime => TimeSpan.FromMinutes(options.Value.CodeLifetimeMinutes);

    public int MaxAttempts => options.Value.MaxAttempts;

    public TimeSpan ResendSpacing => TimeSpan.FromSeconds(options.Value.ResendSpacingSeconds);

    public int DestinationSendsPerHour => options.Value.DestinationSendsPerHour;

    public string? DefaultCountryCode =>
        string.IsNullOrWhiteSpace(options.Value.DefaultCountryCode) ? null : options.Value.DefaultCountryCode.Trim();
}
