using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Intelibill.Application.Common.Behaviours;

/// <summary>
/// Wolverine middleware that automatically runs FluentValidation for any message
/// that has a registered IValidator<TMessage>. This middleware integrates with the
/// Wolverine pipeline using the IMiddleware interface.
/// 
/// Register as scoped service: services.AddScoped<ValidationMiddleware>();
/// The middleware short-circuits on validation failures by returning ErrorOr from BeforeAsync.
/// </summary>
public class ValidationMiddleware
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationMiddleware(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<(HandlerContinuation, ErrorOr<TResponse>?)?> BeforeAsync<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken)
    {
        // Get the validator for this message type from DI
        using var scope = _serviceProvider.CreateScope();
        var validator = scope.ServiceProvider.GetService(typeof(IValidator<TMessage>)) as IValidator<TMessage>;
        
        if (validator is null)
            return null; // No validator, continue to handler

        var result = await validator.ValidateAsync(message, cancellationToken);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

            // Short-circuit: return validation errors instead of calling handler
            var errorOr = ErrorOr<TResponse>.From(errors);
            return (HandlerContinuation.Stop, errorOr);
        }

        return null; // Continue to handler
    }
}

