using System.Reflection;
using System.Xml.Linq;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.P0103ConsumerRegressionTests;

public sealed class BuildBoundaryTests
{
    private const string Shared = "src/shared/backend";
    private const string Contracts = "Intelibill.Shared.Contracts";
    private const string Security = "Intelibill.Shared.Security";

    [Fact]
    public void ProductionConsumersPreserveBoundariesAndDockerRestoreClosure()
    {
        var merchant = Project("src/backend", "Intelibill.Infrastructure");
        var licensing = Project("src/licensing", "Intelibill.Licensing.Infrastructure");
        var security = Project(Shared, Security);
        var contracts = Project(Shared, Contracts);
        Assert.Contains(security, References(merchant));
        Assert.Contains(security, References(licensing));
        Assert.Equal(contracts, Assert.Single(References(security)));
        Assert.Empty(References(contracts));
        Assert.Equal("BCrypt.Net-Next", Assert.Single(Items(security, "PackageReference")));
        Assert.Empty(Items(contracts, "PackageReference"));

        var merchantDomain = Project("src/backend", "Intelibill.Domain");
        var licensingDomain = Project("src/licensing", "Intelibill.Licensing.Domain");
        Assert.Equal("ErrorOr", Assert.Single(Items(merchantDomain, "PackageReference")));
        Assert.Empty(Items(licensingDomain, "PackageReference"));
        Assert.Empty(References(merchantDomain));
        Assert.Empty(References(licensingDomain));
        foreach (var project in new[] { merchantDomain, licensingDomain, security, contracts })
            Assert.Empty(Items(project, "FrameworkReference"));

        AssertFrameworkNeutral(typeof(Intelibill.Domain.Entities.User).Assembly);
        AssertFrameworkNeutral(Assembly.Load("Intelibill.Licensing.Domain"));
        AssertFrameworkNeutral(typeof(OpaqueTokenPrimitives).Assembly);
        AssertFrameworkNeutral(typeof(OpaqueTokenV1).Assembly);
        AssertDockerRestoreInputs();
    }

    private static void AssertFrameworkNeutral(Assembly assembly) =>
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), dependency =>
            dependency.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            dependency.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

    private static void AssertDockerRestoreInputs()
    {
        foreach (var image in new[] { "Dockerfile", "Dockerfile.migrate" })
        {
            var lines = File.ReadLines(Path.Combine(SecurityFixtures.Root, "src/backend", image));
            var restoreInputs = lines.TakeWhile(line => !line.StartsWith("RUN dotnet restore ", StringComparison.Ordinal));
            foreach (var project in new[] { Security, Contracts })
            {
                Assert.Contains(restoreInputs,
                    line => line.StartsWith("COPY ", StringComparison.Ordinal) &&
                        line.Contains($"{Shared}/{project}/{project}.csproj", StringComparison.Ordinal));
            }
        }
    }

    private static string Project(string parent, string name) =>
        Path.Combine(SecurityFixtures.Root, parent, name, name + ".csproj");

    private static IEnumerable<string> References(string project) =>
        Items(project, "ProjectReference").Select(path =>
            Path.GetFullPath(path.Replace('\\', '/'), Path.GetDirectoryName(project)!));

    private static IEnumerable<string> Items(string project, string item) =>
        XDocument.Load(project).Descendants(item).Select(element => (string)element.Attribute("Include")!);
}
