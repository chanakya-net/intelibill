using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.FluentValidation;

namespace Intelibill.Api.Unit.Tests.Wolverine;

public sealed class WolverineValidationConfigurationTests
{
    [Fact]
    public async Task BuiltInFluentValidationMiddleware_DoesNotBreakErrorOrHandlerCodeGeneration()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IValidator<ValidatedPing>, ValidatedPingValidator>();
            })
            .UseWolverine(opts =>
            {
                opts.Discovery.IncludeType<ValidatedPingHandler>();
                opts.UseFluentValidation(RegistrationBehavior.ExplicitRegistration);
            })
            .StartAsync();

        var bus = host.Services.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<ErrorOr<string>>(new ValidatedPing("ok"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task BuiltInFluentValidationMiddleware_ThrowsValidationExceptionForInvalidMessages()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IValidator<ValidatedPing>, ValidatedPingValidator>();
            })
            .UseWolverine(opts =>
            {
                opts.Discovery.IncludeType<ValidatedPingHandler>();
                opts.UseFluentValidation(RegistrationBehavior.ExplicitRegistration);
            })
            .StartAsync();

        var bus = host.Services.GetRequiredService<IMessageBus>();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            bus.InvokeAsync<ErrorOr<string>>(new ValidatedPing(string.Empty), CancellationToken.None));

        var failure = Assert.Single(exception.Errors);
        Assert.Equal(nameof(ValidatedPing.Value), failure.PropertyName);
    }

    public sealed record ValidatedPing(string Value);

    public sealed class ValidatedPingValidator : AbstractValidator<ValidatedPing>
    {
        public ValidatedPingValidator()
        {
            RuleFor(ping => ping.Value).NotEmpty();
        }
    }

    public sealed class ValidatedPingHandler
    {
        public static ErrorOr<string> Handle(ValidatedPing ping) => ping.Value;
    }
}
