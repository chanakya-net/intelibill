namespace Intelibill.Domain.Attributes;

/// <summary>
/// Marks a property as containing sensitive/PII data.
/// Serilog's <c>SensitiveDataDestructuringPolicy</c> replaces its value with "***MASKED***".
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SensitiveDataAttribute : Attribute;
