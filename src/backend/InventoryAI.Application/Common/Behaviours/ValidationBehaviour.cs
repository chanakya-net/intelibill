using ErrorOr;
using FluentValidation;
using Wolverine;

namespace InventoryAI.Application.Common.Behaviours;

/// <summary>
/// Wolverine middleware that runs FluentValidation before any handler.
/// Returns a validation Error via the ErrorOr result if validation fails.
/// </summary>
public class ValidationBehaviour<TMessage>(IEnumerable<IValidator<TMessage>> validators)
    where TMessage : notnull
{
    public async Task<IErrorOr> BeforeAsync(TMessage message, IMessageContext context, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return Error.Unexpected(); // no validators → continue

        var validationContext = new ValidationContext<TMessage>(message);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(validationContext, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            var errors = failures
                .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
                .ToList();

            return ErrorOrFactory.From<object>(errors);
        }

        return ErrorOrFactory.From<object>(new object()); // continue
    }
}
