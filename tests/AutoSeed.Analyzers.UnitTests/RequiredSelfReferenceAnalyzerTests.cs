using EFCore.AutoSeed.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace EFCore.AutoSeed.Analyzers.UnitTests;

public sealed class RequiredSelfReferenceAnalyzerTests
{
    [Fact]
    public async Task RequiredSelfReferencingForeignKey_IsFlagged()
    {
        const string source = """
            public class Employee
            {
                public int Id { get; set; }
                public int {|#0:ManagerId|} { get; set; }
                public Employee Manager { get; set; } = null!;
            }
            """;

        DiagnosticResult expected = new DiagnosticResult(RequiredSelfReferenceAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Employee", "ManagerId", "Manager");

        await Verify(source, expected);
    }

    [Fact]
    public async Task NullableSelfReferencingForeignKey_IsNotFlagged()
    {
        const string source = """
            public class Employee
            {
                public int Id { get; set; }
                public int? ManagerId { get; set; }
                public Employee? Manager { get; set; }
            }
            """;

        await Verify(source);
    }

    [Fact]
    public async Task RequiredForeignKeyToDifferentEntity_IsNotFlagged()
    {
        const string source = """
            public class Customer
            {
                public int Id { get; set; }
            }

            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
                public Customer Customer { get; set; } = null!;
            }
            """;

        await Verify(source);
    }

    [Fact]
    public async Task SelfTypedPropertyWithoutMatchingForeignKey_IsNotFlagged()
    {
        const string source = """
            public class Node
            {
                public int Id { get; set; }
                public Node Root { get; set; } = null!;
            }
            """;

        await Verify(source);
    }

    [Fact]
    public async Task GuidSelfReferencingForeignKey_IsFlagged()
    {
        const string source = """
            using System;

            public class Category
            {
                public Guid Id { get; set; }
                public Guid {|#0:ParentId|} { get; set; }
                public Category Parent { get; set; } = null!;
            }
            """;

        DiagnosticResult expected = new DiagnosticResult(RequiredSelfReferenceAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Category", "ParentId", "Parent");

        await Verify(source, expected);
    }

    private static async Task Verify(string source, params DiagnosticResult[] expected)
    {
        CSharpAnalyzerTest<RequiredSelfReferenceAnalyzer, DefaultVerifier> test = new()
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }
}
