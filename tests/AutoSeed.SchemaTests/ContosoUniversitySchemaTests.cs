using EFCore.AutoSeed;
using EFCore.AutoSeed.SchemaTests;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.SchemaTests.ContosoUniversity;

public sealed class ContosoUniversitySchemaTests
{
    [Fact]
    public async Task Explain_DoesNotThrowAndCoversEveryEntityType()
    {
        using ContosoUniversityContext context = new(SchemaTestSupport.UniqueDatabaseName());

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 200);

        Assert.Contains(typeof(Department).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Course).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(CourseAssignment).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Enrollment).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(OfficeAssignment).FullName!, result.RowCounts.Keys);

        string report = result.ToReport();
        Assert.Contains("Enrollment", report);
        Assert.Contains("CourseAssignment", report);
    }

    [Fact]
    public async Task AutoSeedAsync_SeedsWithoutViolatingReferentialIntegrity()
    {
        using ContosoUniversityContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        SchemaTestSupport.AssertReferentialIntegrityHolds(context);
        Assert.True(await context.Enrollments.AnyAsync());
        Assert.True(await context.CourseAssignments.AnyAsync());

        List<string> departmentNames = await context.Departments.Select(department => department.Name).ToListAsync();
        Assert.Equal(departmentNames.Count, departmentNames.Distinct(StringComparer.Ordinal).Count());

        List<Grade?> grades = await context.Enrollments.Select(enrollment => enrollment.Grade).ToListAsync();
        Assert.Contains(grades, grade => grade is not null);
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameRowCounts()
    {
        using ContosoUniversityContext first = new(SchemaTestSupport.UniqueDatabaseName());
        using ContosoUniversityContext second = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 99, scale: 150);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 99, scale: 150);

        Assert.Equal(firstResult, secondResult);
    }
}

public sealed class ContosoUniversityContext(string databaseName) : DbContext
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<OfficeAssignment> OfficeAssignments => Set<OfficeAssignment>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseAssignment> CourseAssignments => Set<CourseAssignment>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Department>().HasIndex(department => department.Name).IsUnique();
        modelBuilder.Entity<Department>().Property(department => department.Budget).HasPrecision(18, 2);

        modelBuilder.Entity<OfficeAssignment>().HasKey(office => office.InstructorId);
        modelBuilder.Entity<Instructor>()
            .HasOne(instructor => instructor.OfficeAssignment)
            .WithOne(office => office.Instructor)
            .HasForeignKey<OfficeAssignment>(office => office.InstructorId);

        modelBuilder.Entity<CourseAssignment>().HasKey(assignment => new { assignment.CourseId, assignment.InstructorId });

        modelBuilder.Entity<Department>()
            .HasOne(department => department.Administrator)
            .WithMany()
            .HasForeignKey(department => department.AdministratorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Budget { get; set; }
    public DateTime StartDate { get; set; }
    public int? AdministratorId { get; set; }
    public Instructor? Administrator { get; set; }
    public List<Course> Courses { get; set; } = [];
}

public sealed class Instructor
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime HireDate { get; set; }
    public OfficeAssignment? OfficeAssignment { get; set; }
    public List<CourseAssignment> CourseAssignments { get; set; } = [];
}

public sealed class OfficeAssignment
{
    public int InstructorId { get; set; }
    public Instructor Instructor { get; set; } = null!;
    public string Location { get; set; } = "";
}

public sealed class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public List<CourseAssignment> CourseAssignments { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
}

public sealed class CourseAssignment
{
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int InstructorId { get; set; }
    public Instructor Instructor { get; set; } = null!;
}

public sealed class Student
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime EnrollmentDate { get; set; }
    public List<Enrollment> Enrollments { get; set; } = [];
}

public enum Grade
{
    A,
    B,
    C,
    D,
    F,
}

public sealed class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public Grade? Grade { get; set; }
}
