using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.RequiredProperties;

public sealed class RequiredPropertyCoverageTests
{
    private static int _databaseCounter;

    [Fact]
    public async Task AutoSeedAsync_PopulatesRequiredPropertiesNoNameSpecificRuleRecognizes()
    {
        using AppointmentContext context = NewContext();

        await context.AutoSeedAsync(seed: 42, scale: 10);

        List<Appointment> appointments = await context.Appointments.ToListAsync();
        Assert.NotEmpty(appointments);
        Assert.All(appointments, appointment =>
        {
            Assert.NotEqual(default, appointment.ExpiresAt);
            Assert.NotEqual(default, appointment.FollowUpDate);
            Assert.NotEqual(default, appointment.ReminderTime);
            Assert.NotEmpty(appointment.Attachment);
        });
    }

    [Fact]
    public async Task AutoSeedAsync_WithARequiredPropertyNoRuleRecognizes_ThrowsUnsupportedPropertyException()
    {
        using GradeContext context = NewGradeContext();

        UnsupportedPropertyException exception = await Assert.ThrowsAsync<UnsupportedPropertyException>(
            () => context.AutoSeedAsync(seed: 42, scale: 10));

        Assert.Equal("Grade", exception.PropertyName);
        Assert.Contains("Student", exception.EntityTypeName);
        Assert.Equal(0, await context.Students.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithARequiredComplexProperty_ThrowsUnsupportedPropertyException()
    {
        using InvoiceContext context = NewInvoiceContext();

        UnsupportedPropertyException exception = await Assert.ThrowsAsync<UnsupportedPropertyException>(
            () => context.AutoSeedAsync(seed: 42, scale: 10));

        Assert.Equal("Total", exception.PropertyName);
        Assert.Contains("Invoice", exception.EntityTypeName);
        Assert.Equal(0, await context.Invoices.CountAsync());
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// EF Core 9 (this library's other supported major) does not support an optional complex
    /// property at all: <c>IsRequired(false)</c> on one throws <see cref="InvalidOperationException"/>
    /// at model validation time, before this library ever runs. Reaching a nested complex property's
    /// own path needs the outer one to be optional, so this shape can only be built on EF Core 10.
    /// </summary>
    [Fact]
    public async Task AutoSeedAsync_WithANestedRequiredComplexProperty_NamesTheFullPath()
    {
        using NestedComplexPropertyContext context = NewNestedComplexPropertyContext();

        UnsupportedPropertyException exception = await Assert.ThrowsAsync<UnsupportedPropertyException>(
            () => context.AutoSeedAsync(seed: 42, scale: 10));

        Assert.Equal("Total.Currency", exception.PropertyName);
    }
#endif

    private static AppointmentContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new AppointmentContext($"{nameof(AppointmentContext)}_{id}");
    }

    private static GradeContext NewGradeContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new GradeContext($"{nameof(GradeContext)}_{id}");
    }

    private static InvoiceContext NewInvoiceContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new InvoiceContext($"{nameof(InvoiceContext)}_{id}");
    }

#if NET10_0_OR_GREATER
    private static NestedComplexPropertyContext NewNestedComplexPropertyContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new NestedComplexPropertyContext($"{nameof(NestedComplexPropertyContext)}_{id}");
    }
#endif
}

public sealed class AppointmentContext(string databaseName) : DbContext
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class Appointment
{
    public int Id { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateOnly FollowUpDate { get; set; }
    public TimeOnly ReminderTime { get; set; }
    public byte[] Attachment { get; set; } = [];
}

public sealed class GradeContext(string databaseName) : DbContext
{
    public DbSet<Student> Students => Set<Student>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public char Grade { get; set; }
}

public sealed class InvoiceContext(string databaseName) : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Invoice>().ComplexProperty(invoice => invoice.Total);
}

public sealed class Invoice
{
    public int Id { get; set; }
    public Money Total { get; set; }
}

public readonly record struct Money(decimal Amount, string Currency);

#if NET10_0_OR_GREATER
public sealed class NestedComplexPropertyContext(string databaseName) : DbContext
{
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PurchaseOrder>().ComplexProperty(order => order.Total, total =>
        {
            total.IsRequired(false);
            total.ComplexProperty(price => price.Currency);
        });
}

public sealed class PurchaseOrder
{
    public int Id { get; set; }
    public Price Total { get; set; } = new();
}

public sealed class Price
{
    public decimal Amount { get; set; }
    public CurrencyInfo Currency { get; set; } = new();
}

public sealed class CurrencyInfo
{
    public string Code { get; set; } = "";
}
#endif
