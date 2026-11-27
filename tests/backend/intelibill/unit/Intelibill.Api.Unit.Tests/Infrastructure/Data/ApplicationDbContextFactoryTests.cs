using Intelibill.Infrastructure.Data;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Data;

public class ApplicationDbContextFactoryTests
{
    [Fact]
    public void ResolveConfigurationBasePath_WithoutSourceTree_UsesWorkingDirectory()
    {
        var standaloneWorkingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"intelibill-standalone-bundle-{Guid.NewGuid():N}");

        var result = ApplicationDbContextFactory.ResolveConfigurationBasePath(
            standaloneWorkingDirectory);

        Assert.Equal(Path.GetFullPath(standaloneWorkingDirectory), result);
    }
}
