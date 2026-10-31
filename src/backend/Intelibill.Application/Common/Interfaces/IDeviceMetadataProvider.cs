using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// What the transport observed about the device making this request, so a new session can be
/// labelled with something its owner will recognise. Description only: nothing here authorises
/// anything, and a request that repeats another device's values gains nothing by it.
/// </summary>
public interface IDeviceMetadataProvider
{
    DeviceMetadata Current { get; }
}
