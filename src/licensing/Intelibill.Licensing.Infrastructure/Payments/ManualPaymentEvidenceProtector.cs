using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Intelibill.Licensing.Application.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class ManualReceivingAccountOptions
{
    public bool Enabled { get; set; }
    public string ReceivingAccountId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Environment { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public string EvidencePolicyVersion { get; set; } = "";
    public string ReferenceKey { get; set; } = "";
    public string ReferencePattern { get; set; } = "";
    public string EvidencePattern { get; set; } = "";
}

internal sealed class ManualPaymentEvidenceProtector(IOptionsSnapshot<ManualReceivingAccountOptions> options,
    IDataProtectionProvider protection) : IManualPaymentEvidence
{
    public ManualReceivingAccount? Account
    {
        get
        {
            var config = options.Value;
            return config.Enabled && config.Environment is "Development" or "Test" or "Live" &&
                ValidText(config.ReceivingAccountId, 80) && ValidText(config.Label, 100) &&
                ValidText(config.ConfigurationVersion, 80) && ValidText(config.EvidencePolicyVersion, 80) &&
                config.ReferencePattern is { Length: > 0 and <= 256 } && config.EvidencePattern is { Length: > 0 and <= 256 } &&
                Key(config) is not null
                ? new(config.ReceivingAccountId, config.Label, config.Environment, config.ConfigurationVersion,
                    config.EvidencePolicyVersion) : null;
        }
    }

    public string? ReferenceDigest(ConfirmManualPayment command)
    {
        var config = options.Value;
        var reference = command.Reference.Trim().ToUpperInvariant();
        if (command.ReceivingAccountId != config.ReceivingAccountId || command.ConfigurationVersion != config.ConfigurationVersion ||
            !ValidText(command.Reason, 400) || Key(config) is not { } key ||
            !Matches(config.ReferencePattern, reference) || !Matches(config.EvidencePattern, command.Evidence.Trim()))
            return null;
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(reference)));
    }

    public string Protect(Guid account, Guid order, Guid receipt, ConfirmManualPayment command) =>
        Protector(account, order, receipt).Protect(Canonical(command));

    public ManualPaymentEvidenceDetails Read(Guid account, StoredManualReceipt receipt)
    {
        var command = JsonSerializer.Deserialize<ConfirmManualPayment>(Unseal(account, receipt))
            ?? throw new CryptographicException("Manual payment evidence cannot be read.");
        return new(command.Reference, command.Evidence, command.Reason);
    }

    public bool Matches(Guid account, StoredManualReceipt receipt, ConfirmManualPayment command)
    {
        var saved = Unseal(account, receipt);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(saved), Encoding.UTF8.GetBytes(Canonical(command)));
    }

    private string Unseal(Guid account, StoredManualReceipt receipt) =>
        Protector(account, receipt.OrderId, receipt.Receipt.ReceiptId).Unprotect(receipt.ProtectedPayload);

    private IDataProtector Protector(Guid account, Guid order, Guid receipt) =>
        protection.CreateProtector("InteliBill.Licensing.ManualPayment/1", account.ToString("D"), order.ToString("D"), receipt.ToString("D"));

    private static string Canonical(ConfirmManualPayment command) => JsonSerializer.Serialize(command with
    {
        Reference = command.Reference?.Trim() ?? "",
        Evidence = command.Evidence?.Trim() ?? "",
        Reason = command.Reason?.Trim() ?? "",
    });

    private static byte[]? Key(ManualReceivingAccountOptions config)
    {
        var key = new byte[32];
        return Convert.TryFromBase64String(config.ReferenceKey, key, out var written) && written == key.Length ? key : null;
    }

    private static bool ValidText(string value, int length) =>
        value.Trim().Length > 0 && value.Length <= length && !value.Any(char.IsControl);

    private static bool Matches(string pattern, string value)
    {
        try
        {
            return !value.Any(char.IsControl) &&
                Regex.IsMatch(value, $@"\A(?:{pattern})\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (Exception error) when (error is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
