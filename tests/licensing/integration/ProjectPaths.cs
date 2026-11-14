namespace Intelibill.Licensing.Integration.Tests;

internal static class ProjectPaths
{
    internal static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
