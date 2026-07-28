using System.Data.Common;
using Intelibill.Infrastructure.Data.Interceptors;
using Intelibill.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Observability;

public sealed class RequestDatabaseCommandInterceptorTests
{
    [Fact]
    public void ReaderExecuted_RecordsProviderReportedDuration()
    {
        var (telemetry, interceptor, request) = CreateInterceptor();
        using (request)
        {
            var command = Substitute.For<DbCommand>();
            var reader = Substitute.For<DbDataReader>();
            var eventData = CreateExecutedEventData(
                command,
                DbCommandMethod.ExecuteReader,
                reader,
                TimeSpan.FromMilliseconds(17));

            var result = interceptor.ReaderExecuted(command, eventData, reader);

            Assert.Same(reader, result);
            Assert.Equal(1, telemetry.CommandCount);
            Assert.Equal(TimeSpan.FromMilliseconds(17), telemetry.CumulativeDuration);
        }
    }

    [Fact]
    public async Task ReaderExecutedAsync_RecordsProviderReportedDuration()
    {
        var (telemetry, interceptor, request) = CreateInterceptor();
        using (request)
        {
            var command = Substitute.For<DbCommand>();
            var reader = Substitute.For<DbDataReader>();
            var eventData = CreateExecutedEventData(
                command,
                DbCommandMethod.ExecuteReader,
                reader,
                TimeSpan.FromMilliseconds(19));

            var result = await interceptor.ReaderExecutedAsync(command, eventData, reader);

            Assert.Same(reader, result);
            Assert.Equal(1, telemetry.CommandCount);
            Assert.Equal(TimeSpan.FromMilliseconds(19), telemetry.CumulativeDuration);
        }
    }

    [Theory]
    [InlineData(DbCommandMethod.ExecuteScalar)]
    [InlineData(DbCommandMethod.ExecuteNonQuery)]
    public void ExecutedScalarAndNonQuery_RecordProviderReportedDuration(DbCommandMethod method)
    {
        var (telemetry, interceptor, request) = CreateInterceptor();
        using (request)
        {
            var command = Substitute.For<DbCommand>();
            var eventData = CreateExecutedEventData(
                command,
                method,
                result: 1,
                TimeSpan.FromMilliseconds(23));

            if (method == DbCommandMethod.ExecuteScalar)
            {
                interceptor.ScalarExecuted(command, eventData, result: 1);
            }
            else
            {
                interceptor.NonQueryExecuted(command, eventData, result: 1);
            }

            Assert.Equal(1, telemetry.CommandCount);
            Assert.Equal(TimeSpan.FromMilliseconds(23), telemetry.CumulativeDuration);
        }
    }

    [Fact]
    public void CommandFailed_RecordsProviderReportedDuration()
    {
        var (telemetry, interceptor, request) = CreateInterceptor();
        using (request)
        {
            var command = Substitute.For<DbCommand>();
            var eventData = CreateErrorEventData(command, TimeSpan.FromMilliseconds(29));

            interceptor.CommandFailed(command, eventData);

            Assert.Equal(1, telemetry.CommandCount);
            Assert.Equal(TimeSpan.FromMilliseconds(29), telemetry.CumulativeDuration);
        }
    }

    [Fact]
    public void CommandCanceled_RecordsProviderReportedDuration()
    {
        var (telemetry, interceptor, request) = CreateInterceptor();
        using (request)
        {
            var command = Substitute.For<DbCommand>();
            var eventData = CreateEndEventData(command, TimeSpan.FromMilliseconds(31));

            interceptor.CommandCanceled(command, eventData);

            Assert.Equal(1, telemetry.CommandCount);
            Assert.Equal(TimeSpan.FromMilliseconds(31), telemetry.CumulativeDuration);
        }
    }

    private static (
        RequestDatabaseTelemetry Telemetry,
        RequestDatabaseCommandInterceptor Interceptor,
        RequestDatabaseTelemetryAccessor.RequestScope Request) CreateInterceptor()
    {
        var accessor = new RequestDatabaseTelemetryAccessor();
        var request = accessor.BeginRequest();
        return (
            request.Telemetry,
            new RequestDatabaseCommandInterceptor(accessor),
            request);
    }

    private static CommandExecutedEventData CreateExecutedEventData(
        DbCommand command,
        DbCommandMethod method,
        object result,
        TimeSpan duration)
    {
        return new CommandExecutedEventData(
            eventDefinition: null!,
            messageGenerator: static (_, _) => string.Empty,
            connection: Substitute.For<DbConnection>(),
            command,
            logCommandText: string.Empty,
            context: null,
            method,
            commandId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            result,
            async: false,
            logParameterValues: false,
            startTime: DateTimeOffset.UtcNow - duration,
            duration,
            commandSource: CommandSource.LinqQuery);
    }

    private static CommandErrorEventData CreateErrorEventData(
        DbCommand command,
        TimeSpan duration)
    {
        return new CommandErrorEventData(
            eventDefinition: null!,
            messageGenerator: static (_, _) => string.Empty,
            connection: Substitute.For<DbConnection>(),
            command,
            logCommandText: string.Empty,
            context: null,
            DbCommandMethod.ExecuteReader,
            commandId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            new InvalidOperationException("Database failure"),
            async: false,
            logParameterValues: false,
            startTime: DateTimeOffset.UtcNow - duration,
            duration,
            commandSource: CommandSource.LinqQuery);
    }

    private static CommandEndEventData CreateEndEventData(
        DbCommand command,
        TimeSpan duration)
    {
        return new CommandEndEventData(
            eventDefinition: null!,
            messageGenerator: static (_, _) => string.Empty,
            connection: Substitute.For<DbConnection>(),
            command,
            logCommandText: string.Empty,
            context: null,
            DbCommandMethod.ExecuteReader,
            commandId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            async: false,
            logParameterValues: false,
            startTime: DateTimeOffset.UtcNow - duration,
            duration,
            commandSource: CommandSource.LinqQuery);
    }
}
