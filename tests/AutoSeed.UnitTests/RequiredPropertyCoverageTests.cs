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
