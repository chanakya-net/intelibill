using System.Diagnostics.CodeAnalysis;

namespace Intelibill.Domain.Exceptions;
[ExcludeFromCodeCoverage]
public class DomainException(string message) : Exception(message);
