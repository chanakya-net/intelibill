using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Api.Extensions;

internal sealed class HttpDeviceMetadataProvider(IHttpContextAccessor httpContextAccessor)
    : IDeviceMetadataProvider
{
    public DeviceMetadata Current
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
                return DeviceMetadata.Unknown;

            // UseForwardedHeaders has already run, so behind ingress this is the client address
            // rather than the proxy's.
            return DeviceMetadata.From(
                context.Request.Headers.UserAgent.ToString(),
                context.Connection.RemoteIpAddress?.ToString());
        }
    }
}
