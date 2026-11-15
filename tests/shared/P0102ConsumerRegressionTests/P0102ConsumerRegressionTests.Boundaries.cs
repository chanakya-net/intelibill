using System.Xml.Linq;
using Intelibill.Shared.Web.Logging;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.P0102ConsumerRegressionTests;

public sealed partial class P0102ConsumerRegressionTests
{
    [Fact]
    public void BothApis_ResolveTheSharedErrorAndDiagnosticTypes()
    {
        var shared = typeof(SafeErrorFactory).Assembly;
        foreach (var host in Hosts(maskPii: true))
        {
            using (host)
            {
                var factory = host.Services.GetRequiredService<ISafeErrorFactory>();
                var writer = host.Services.GetRequiredService<ISafeDiagnosticWriter>();
                output.WriteLine($"{host.Name}: {factory.GetType().FullName}, {writer.GetType().FullName} from {shared.GetName().Name}");
                Assert.Same(shared, factory.GetType().Assembly);
                Assert.Same(shared, writer.GetType().Assembly);
            }
        }
    }

    [Fact]
    public void SharedWeb_HasNoApplicationLayerDependency()
    {
        var shared = typeof(SafeErrorFactory).Assembly;
        var references = shared.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        output.WriteLine($"{shared.GetName().Name} references: {string.Join(", ", references.Order(StringComparer.Ordinal))}");
        Assert.DoesNotContain(references, name => name.StartsWith("Intelibill.", StringComparison.Ordinal));

        var project = XDocument.Load(Path.Combine(
            HostProbes.Root, "src/shared/backend/Intelibill.Shared.Web/Intelibill.Shared.Web.csproj"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }
}
