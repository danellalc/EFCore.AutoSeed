using EFCore.AutoSeed.Exceptions;

namespace EFCore.AutoSeed.UnitTests.Exceptions;

public sealed class UnresolvableCycleExceptionTests
{
    [Fact]
    public void Message_NamesEveryEntityTypeInTheCycle()
    {
        string[] cycle = ["Employee", "Department"];

        var exception = new UnresolvableCycleException(cycle);

        Assert.Contains("Employee", exception.Message);
        Assert.Contains("Department", exception.Message);
        Assert.Equal(cycle, exception.EntityTypeNames);
    }

    [Fact]
    public void IsAnAutoSeedException()
    {
        var exception = new UnresolvableCycleException(["Employee"]);

        Assert.IsAssignableFrom<AutoSeedException>(exception);
    }
}
