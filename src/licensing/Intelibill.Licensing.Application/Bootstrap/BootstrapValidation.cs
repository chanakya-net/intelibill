using System.Buffers;
using System.Net.Mail;
using System.Text;

namespace Intelibill.Licensing.Application.Bootstrap;

public static class BootstrapValidation
{
    private static readonly SearchValues<char> TokenAlphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static string? NormalizeEmail(string? email) =>
        email is { Length: > 0 and <= 254 } && MailAddress.TryCreate(email, out var address)
            && string.Equals(address.Address, email, StringComparison.Ordinal)
            ? email.ToUpperInvariant()
            : null;

    // BCrypt operates on at most 72 UTF-8 bytes. Reject rather than truncate.
    public static bool IsPasswordValid(string? password) =>
        password is { Length: >= 12 and <= 72 } && !password.Contains('\0') && Encoding.UTF8.GetByteCount(password) <= 72;

    public static bool IsProofValid(string? proof) => HasTokenShape(proof, "p1_");

    public static bool IsEnrollmentValid(string? credential) => HasTokenShape(credential, "e1_");

    public static bool IsInvitationValid(string? token) => HasTokenShape(token, "i1_");

    public static bool IsRecoveryResetValid(string? token) => HasTokenShape(token, "k1_");

    private static bool HasTokenShape(string? value, string prefix) =>
        value is { Length: 89 } && value.StartsWith(prefix, StringComparison.Ordinal)
        && value.AsSpan(3).IndexOfAnyExcept(TokenAlphabet) < 0;

    public static bool IsReferenceValid(string? reference) =>
        reference is { Length: >= 4 and <= 128 }
        && reference.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/');

    public static bool IsOriginAllowed(string? origin, bool allowLoopbackHttp) =>
        origin is { Length: <= 255 } && Uri.TryCreate(origin, UriKind.Absolute, out var address)
        && string.Equals(origin, address.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)
        && address.UserInfo.Length == 0
        && (address.Scheme == Uri.UriSchemeHttps
            || (allowLoopbackHttp && address.Scheme == Uri.UriSchemeHttp && address.IsLoopback));
}
