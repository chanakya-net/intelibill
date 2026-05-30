using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Models;

public sealed record ExternalOAuthTokenResult(ExternalAuthProvider Provider, string ProviderToken);
