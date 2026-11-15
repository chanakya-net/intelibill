using System.Collections.Concurrent;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: CollectionBehavior(
    "Intelibill.Integration.Tests.IntegrationCollectionFactory",
    "Intelibill.Integration.Tests")]

namespace Intelibill.Integration.Tests;

/// <summary>
/// Turns <c>[Collection("Integration Tests")]</c> into one collection per test class.
/// </summary>
/// <remarks>
/// xUnit runs the classes of one collection one after another and shares the
/// collection's fixtures between them, which made the whole suite a single queue on
/// a single database. Here each class gets a collection of its own that still
/// carries <see cref="IntegrationTestGroup"/>'s fixtures: its own
/// <see cref="PostgreSqlTestFixture"/>, hence its own database, and it runs beside
/// the other classes. Tests inside a class keep running one at a time.
/// A class under any other collection name, or none, is grouped the way xUnit
/// does by default.
/// </remarks>
public sealed class IntegrationCollectionFactory(ITestAssembly testAssembly, IMessageSink diagnosticMessageSink)
    : IXunitTestCollectionFactory
{
    private readonly ConcurrentDictionary<string, ITestCollection> _collections = new();
    private readonly CollectionPerClassTestCollectionFactory _default = new(testAssembly, diagnosticMessageSink);

    public string DisplayName => "integration-collection-per-class";

    public ITestCollection Get(ITypeInfo testClass)
    {
        var collection = testClass.GetCustomAttributes(typeof(CollectionAttribute)).SingleOrDefault();
        var name = collection?.GetConstructorArguments().FirstOrDefault() as string;
        if (name != IntegrationTestGroup.Name)
        {
            return _default.Get(testClass);
        }

        return _collections.GetOrAdd(
            testClass.Name,
            className => new TestCollection(
                testAssembly,
                Reflector.Wrap(typeof(IntegrationTestGroup)),
                $"{IntegrationTestGroup.Name}: {className}"));
    }
}
