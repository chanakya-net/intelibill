using System.Xml.Linq;

namespace Intelibill.Licensing.Integration.Tests;

internal static class ProjectBoundaryAssertions
{
    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];
    private static readonly string[] StandaloneServiceRoots =
    [
        "src/backend/barcode-master-api", "src/backend/hsn-finder-ai",
        "tests/backend/barcode-master-api", "tests/backend/hsn-finder-ai",
    ];
    private static readonly string[] BuildOverrides =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];
    private static readonly string[] PolicyProperties =
        ["ManagePackageVersionsCentrally", "CentralPackageVersionOverrideEnabled",
         "TreatWarningsAsErrors", "WarningsNotAsErrors", "NoWarn", "RunAnalyzers", "RunAnalyzersDuringBuild",
         "AnalysisLevel", "EnforceCodeStyleInBuild"];

    internal static void Verify()
    {
        var licensing = Path.Combine(ProjectPaths.Root, "src/backend/licensing");
        var shared = Path.Combine(ProjectPaths.Root, "src/backend/shared");
        var tests = Path.Combine(ProjectPaths.Root, "tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj");
        var licensingProjects = Layers.ToDictionary(layer => layer, layer => LayerProject(licensing, layer), StringComparer.Ordinal);
        var pins = Load(Path.Combine(ProjectPaths.Root, "Directory.Packages.props")).Descendants("PackageVersion")
            .Select(package => Attribute(package, "Include")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssertLayerReferences(licensingProjects, shared);
        var domain = Load(licensingProjects["Domain"]);
        Assert.Equal("Microsoft.NET.Sdk", Attribute(domain.Root!, "Sdk"));
        Assert.DoesNotContain(domain.Descendants(), item => item.Name.LocalName is "PackageReference" or "FrameworkReference");

        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in licensingProjects.Values)
            VerifyProjectClosure(project, licensing, shared, pins, closure);

        VerifyCentralPackages(tests, pins);
        var testReferences = ProjectReferences(tests);
        Assert.Contains(licensingProjects["Api"], testReferences);
        Assert.Contains(Path.Combine(ProjectPaths.Root, "src/backend/intelibill/Intelibill.Api/Intelibill.Api.csproj"), testReferences);
        VerifySolutions(licensing, tests, closure);
    }

    private static void AssertLayerReferences(Dictionary<string, string> projects, string shared)
    {
        Assert.Empty(ProjectReferences(projects["Domain"]));
        AssertLicensingReferences("Application", ["Domain"]);
        AssertLicensingReferences("Infrastructure", ["Application", "Domain"]);
        AssertLicensingReferences("Api", ["Application", "Infrastructure"]);

        void AssertLicensingReferences(string from, string[] expected)
        {
            var references = ProjectReferences(projects[from]).Where(path => !IsUnder(path, shared));
            Assert.Equal(expected.Select(layer => projects[layer]).Order(StringComparer.Ordinal),
                references.Order(StringComparer.Ordinal));
        }
    }

    private static void VerifyProjectClosure(
        string project, string licensing, string shared, HashSet<string> pins, HashSet<string> seen)
    {
        Assert.True(IsUnder(project, licensing) || IsUnder(project, shared), $"Unexpected production dependency: {project}");
        if (!seen.Add(project))
            return;

        VerifyCentralPackages(project, pins);
        var document = Load(project);
        var directory = Path.GetDirectoryName(project)!;
        Assert.DoesNotContain(document.Descendants(), item => PolicyProperties.Contains(item.Name.LocalName));
        Assert.Empty(document.Descendants("Import"));
        Assert.Empty(document.Descendants("Reference"));
        foreach (var item in document.Descendants("Compile"))
        {
            foreach (var property in new[] { "Include", "Update" })
                Assert.All(ResolveItems(directory, Attribute(item, property)), path => Assert.True(IsUnder(path, directory)));
        }

        foreach (var reference in ProjectReferences(project))
        {
            if (IsUnder(project, shared))
                Assert.True(IsUnder(reference, shared), $"Shared project depends on application code: {reference}");
            VerifyProjectClosure(reference, licensing, shared, pins, seen);
        }
    }

    private static void VerifyCentralPackages(string project, HashSet<string> pins)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(project)!);
             directory.FullName != ProjectPaths.Root; directory = directory.Parent!)
        {
            Assert.All(BuildOverrides, name => Assert.False(File.Exists(Path.Combine(directory.FullName, name))));
        }

        var document = Load(project);
        Assert.Empty(document.Descendants("PackageVersion"));
        foreach (var reference in document.Descendants("PackageReference"))
        {
            Assert.Contains(Attribute(reference, "Include"), pins);
            foreach (var property in new[] { "Version", "VersionOverride" })
            {
                Assert.Null(reference.Attribute(property));
                Assert.Null(reference.Element(property));
            }
        }
    }

    private static void VerifySolutions(string licensing, string tests, HashSet<string> production)
    {
        var merchant = SolutionProjects(Path.Combine(ProjectPaths.Root, "src/backend/intelibill/Intelibill.slnx"));
        var licensingSolution = SolutionProjects(Path.Combine(licensing, "Intelibill.Licensing.slnx"));
        var aggregate = SolutionProjects(Path.Combine(ProjectPaths.Root, "Intelibill.slnx"));

        Assert.Equal(production.Append(tests).Order(StringComparer.Ordinal), licensingSolution.Order(StringComparer.Ordinal));
        // Cross-consumer regression projects under tests/backend/shared belong to neither sub-solution.
        var sharedTests = Path.Combine(ProjectPaths.Root, "tests/backend/shared");
        var crossConsumerTests = aggregate.Where(path => IsUnder(path, sharedTests)).ToArray();
        Assert.All(crossConsumerTests, path => Assert.Equal("true", (string?)Load(path).Descendants("IsTestProject").SingleOrDefault()));
        var standalone = StandaloneServiceProjects();
        Assert.Equal(merchant.Union(licensingSolution, StringComparer.Ordinal).Union(crossConsumerTests, StringComparer.Ordinal)
            .Union(standalone, StringComparer.Ordinal).Order(StringComparer.Ordinal), aggregate.Order(StringComparer.Ordinal));

        // The standalone services are part of the root solution only: neither backend sub-solution builds them.
        Assert.DoesNotContain(merchant, standalone.Contains);
        Assert.DoesNotContain(licensingSolution, standalone.Contains);
    }

    // Every project under the standalone service folders, discovered from disk so that a new one must be added to the
    // root solution. Each folder keeps its own package versions and build settings, so root CPM never applies to them.
    private static string[] StandaloneServiceProjects()
    {
        var projects = StandaloneServiceRoots
            .Select(root => Path.Combine(ProjectPaths.Root, root))
            .SelectMany(root =>
            {
                var manifest = Path.Combine(root, "Directory.Packages.props");
                Assert.True(File.Exists(manifest), $"Missing isolating package manifest: {Path.GetRelativePath(ProjectPaths.Root, manifest)}");
                Assert.Equal("false", (string?)Load(manifest).Descendants("ManagePackageVersionsCentrally").SingleOrDefault());
                return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
            })
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(projects);
        return projects;
    }

    private static string LayerProject(string directory, string layer)
    {
        var name = "Intelibill.Licensing." + layer;
        return Path.Combine(directory, name, name + ".csproj");
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static XDocument Load(string path)
    {
        Assert.True(File.Exists(path), $"Expected repository file: {Path.GetRelativePath(ProjectPaths.Root, path)}");
        return XDocument.Load(path);
    }

    private static string[] ProjectReferences(string file) => Load(file).Descendants("ProjectReference")
        .SelectMany(item => ResolveItems(Path.GetDirectoryName(file)!, Attribute(item, "Include"))).ToArray();

    private static string[] SolutionProjects(string file) => Load(file).Descendants("Project")
        .SelectMany(item => ResolveItems(Path.GetDirectoryName(file)!, Attribute(item, "Path"))).ToArray();

    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    private static IEnumerable<string> ResolveItems(string directory, string paths) =>
        paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.GetFullPath(path.Replace('\\', '/'), directory));
}
