using EFCore.AutoSeed.Cli;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EFCore.AutoSeed.UnitTests.Cli;

public sealed class CliApplicationTests
{
    private static readonly string TestAssemblyPath = typeof(CliApplicationTests).Assembly.Location;
    private static readonly string ContextTypeName = typeof(CliTestDbContext).FullName ?? nameof(CliTestDbContext);

    [Fact]
    public async Task RunAsync_WithNoArguments_PrintsUsageAndSucceeds()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync([]);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("autoseed", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task RunAsync_WithAnUnknownCommand_FailsWithUsageError()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(["bogus"]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("bogus", error);
    }

    [Fact]
    public async Task RunAsync_ExplainWithoutContext_FailsWithUsageError()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(["explain", "--assembly", TestAssemblyPath]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("--context", error);
    }

    [Fact]
    public async Task RunAsync_ExplainWithoutAssembly_FailsWithUsageError()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(["explain", "--context", ContextTypeName]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("--assembly", error);
    }

    [Fact]
    public async Task RunAsync_ExplainWithAMissingAssemblyFile_FailsWithContextResolutionError()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
            ["explain", "--context", ContextTypeName, "--assembly", "does-not-exist.dll"]);

        Assert.Equal(CliExitCodes.ContextResolutionError, exitCode);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ExplainWithAnUnknownContextType_FailsWithContextResolutionError()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
            ["explain", "--context", "Nope.Nonexistent", "--assembly", TestAssemblyPath]);

        Assert.Equal(CliExitCodes.ContextResolutionError, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task RunAsync_ExplainWithARealContext_PrintsAPlanAndSucceeds()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
            ["explain", "--context", ContextTypeName, "--assembly", TestAssemblyPath, "--seed", "7", "--scale", "5"]);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(error);
        Assert.Contains("CliCustomer", output);
        Assert.Contains("CliOrder", output);
        Assert.Contains("rows", output);
    }

    [Fact]
    public void DbContextLoader_WhenTheDesignTimeFactoryReturnsNull_FailsWithANonEmptyMessage()
    {
        DbContextLoadResult result = DbContextLoader.Load(TestAssemblyPath, typeof(NullFactoryContext).FullName ?? nameof(NullFactoryContext));

        Assert.False(result.Succeeded);
        Assert.Null(result.Context);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Contains("CreateDbContext", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ExplainWithAModelEfCoreCannotBuild_FailsCleanlyRatherThanCrashing()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
            ["explain", "--context", typeof(BrokenModelContext).FullName ?? nameof(BrokenModelContext), "--assembly", TestAssemblyPath]);

        Assert.NotEqual(CliExitCodes.Success, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain("at EFCore.AutoSeed", error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCapturedAsync(string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        StringWriter capturedOut = new();
        StringWriter capturedError = new();

        try
        {
            Console.SetOut(capturedOut);
            Console.SetError(capturedError);
            int exitCode = await CliApplication.RunAsync(args).ConfigureAwait(false);
            return (exitCode, capturedOut.ToString(), capturedError.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

public sealed class CliTestDbContext : DbContext
{
    public DbSet<CliCustomer> Customers => Set<CliCustomer>();
    public DbSet<CliOrder> Orders => Set<CliOrder>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(CliTestDbContext));
}

public sealed class CliCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
}

public sealed class CliOrder
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public CliCustomer Customer { get; set; } = null!;
}

public sealed class NullFactoryContext : DbContext
{
    internal NullFactoryContext(DbContextOptions<NullFactoryContext> options)
        : base(options)
    {
    }
}

public sealed class NullFactoryContextFactory : IDesignTimeDbContextFactory<NullFactoryContext>
{
    public NullFactoryContext CreateDbContext(string[] args) => null!;
}

public sealed class BrokenModelContext : DbContext
{
    public DbSet<MismatchedChild> Children => Set<MismatchedChild>();
    public DbSet<MismatchedParent> Parents => Set<MismatchedParent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MismatchedChild>()
            .HasOne(child => child.Parent)
            .WithMany()
            .HasForeignKey(child => child.ParentId)
            .HasPrincipalKey(parent => parent.Code);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(BrokenModelContext));
}

public sealed class MismatchedParent
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public sealed class MismatchedChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public MismatchedParent Parent { get; set; } = null!;
}
