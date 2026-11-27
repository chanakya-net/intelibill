using Intelibill.Api.Extensions;
using JasperFx;
using JasperFx.CodeGeneration;

namespace Intelibill.Api.Unit.Tests.Wolverine;

public sealed class WolverineCodeGenerationTests
{
    [Fact]
    public void ConfigureForProduction_RequiresPreGeneratedTypes()
    {
        var options = new JasperFxOptions();

        WolverineCodeGenerationExtensions.ConfigureForProduction(options);

        Assert.Equal(TypeLoadMode.Static, options.Production.GeneratedCodeMode);
        Assert.True(options.Production.AssertAllPreGeneratedTypesExist);
    }

    [Fact]
    public void ConfigureForProduction_LeavesDevelopmentDynamic()
    {
        var options = new JasperFxOptions();

        WolverineCodeGenerationExtensions.ConfigureForProduction(options);

        Assert.Equal(TypeLoadMode.Dynamic, options.Development.GeneratedCodeMode);
        Assert.False(options.Development.AssertAllPreGeneratedTypesExist);
    }
}
