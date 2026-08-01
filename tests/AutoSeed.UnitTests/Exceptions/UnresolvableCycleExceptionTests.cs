using EFCore.AutoSeed.Exceptions;

namespace EFCore.AutoSeed.UnitTests.Exceptions;

public sealed class UnresolvableCycleExceptionTests
{
    [Fact]
    public void Message_NamesEveryEntityTypeInEveryCycle()
    {
        IReadOnlyList<IReadOnlyList<string>> cycles = [["Employee", "Department"], ["Order", "Invoice"]];

        var exception = new UnresolvableCycleException(cycles);

        Assert.Contains("Employee", exception.Message);
        Assert.Contains("Department", exception.Message);
        Assert.Contains("Order", exception.Message);
        Assert.Contains("Invoice", exception.Message);
        Assert.Equal(cycles, exception.Cycles);
    }

    [Fact]
    public void Message_ReportsHowManyCyclesWereFound()
    {
        IReadOnlyList<IReadOnlyList<string>> cycles = [["A", "B"], ["X", "Y"]];

        var exception = new UnresolvableCycleException(cycles);

        Assert.Contains("2 unresolvable cycles", exception.Message);
    }

    [Fact]
    public void IsAnAutoSeedException()
    {
        var exception = new UnresolvableCycleException([["Employee"]]);

        Assert.IsAssignableFrom<AutoSeedException>(exception);
    }

    [Fact]
    public void Constructor_WithNullCycles_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UnresolvableCycleException(null!));
    }
}
