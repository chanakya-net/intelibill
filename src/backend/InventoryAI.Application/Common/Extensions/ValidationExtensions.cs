using ErrorOr;
using FluentValidation;

namespace InventoryAI.Application.Common.Extensions;

public static class ValidationExtensions
{
    public static async Task<ErrorOr<Success>?> ValidateCommandAsync<T>(this IValidator<T>? validator, T command, CancellationToken cancellationToken = default)
    {
        if (validator is null) return null;

        var result = await validator.ValidateAsync(command, cancellationToken);
        if (result.IsValid) return null;

        var errors = result.Errors
            .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
            .ToList();

        return errors;
    }
}
