using System.Reflection;
using Intelibill.Domain.Attributes;
using Serilog.Core;
using Serilog.Events;

namespace Intelibill.Api.Logging;

/// <summary>
/// Replaces any property decorated with <see cref="SensitiveDataAttribute"/> with "***MASKED***"
/// when Serilog destructures an object for structured logging.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private const string MaskedValue = "***MASKED***";

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        var type = value.GetType();

        // Only intercept reference types with at least one [SensitiveData] property.
        if (type.IsPrimitive || type == typeof(string))
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        if (!properties.Any(p => p.IsDefined(typeof(SensitiveDataAttribute), inherit: true)))
        {
            result = null!;
            return false;
        }

        var logProperties = properties.Select(p =>
        {
            if (p.IsDefined(typeof(SensitiveDataAttribute), inherit: true))
                return new LogEventProperty(p.Name, new ScalarValue(MaskedValue));

            var propValue = p.GetValue(value);
            return new LogEventProperty(
                p.Name,
                propertyValueFactory.CreatePropertyValue(propValue, destructureObjects: true));
        }).ToList();

        result = new StructureValue(logProperties);
        return true;
    }
}
