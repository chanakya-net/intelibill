namespace Intelibill.Api.Middleware.RateLimiting;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class DisableRateLimitAttribute : Attribute;
