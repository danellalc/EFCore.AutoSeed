using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFCore.AutoSeed.Analyzers;

/// <summary>
/// Flags a required (non-nullable) foreign key property that points back at its own containing
/// entity type by EF Core naming convention: a scalar property named "&lt;Navigation&gt;Id" next
/// to a reference navigation property named "&lt;Navigation&gt;" whose type is the entity itself.
/// </summary>
/// <remarks>
/// <para>
/// AutoSeed's runtime pipeline (<c>EFCore.AutoSeed.Pipeline.CycleResolver</c>) already rejects this
/// exact shape with an <c>UnresolvableCycleException</c>: a self-referencing foreign key that is
/// required can never be satisfied for the row inserted first, because no earlier row of the same
/// entity exists yet for it to point to. Unlike a required cycle between two different entities,
/// which can sometimes be broken by seeding one side first and filling the reference in on a second
/// pass, a required self-reference has no earlier row, at any point, ever. This analyzer reports
/// the same problem at compile time, directly on the C# source, before a database connection is
/// ever opened.
/// </para>
/// <para>
/// This is a convention-based, property-shape heuristic, not a full reading of the EF Core model:
/// </para>
/// <list type="bullet">
/// <item><description>
/// It only recognizes the "&lt;Navigation&gt;Id" foreign key naming convention, not the
/// "&lt;Navigation&gt;&lt;PrincipalKeyPropertyName&gt;" alternative EF Core also supports (for
/// example, a principal key named <c>Code</c> would conventionally pair with a foreign key named
/// <c>ManagerCode</c>, which this analyzer does not recognize).
/// </description></item>
/// <item><description>
/// It cannot see fluent API configuration in <c>OnModelCreating</c>. A property this analyzer
/// flags as required could be made optional at the database level with
/// <c>.HasForeignKey(...).IsRequired(false)</c>; a nullable property left unflagged could likewise
/// be forced required with <c>.IsRequired()</c>. A fluent-API-aware version is future work.
/// </description></item>
/// <item><description>
/// It only looks at members declared directly on the class being analyzed, not ones inherited from
/// a base class.
/// </description></item>
/// </list>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiredSelfReferenceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID reported by this analyzer.
    /// </summary>
    public const string DiagnosticId = "AUTOSEED001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Required self-referencing foreign key can never be satisfied",
        messageFormat: "'{0}.{1}' is a required (non-nullable) foreign key back to '{0}' itself, via navigation '{2}'; AutoSeed can never generate a value for the first row of a self-referencing entity when the key is required. Make '{1}' nullable, or exclude '{0}' from seeding.",
        category: "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A self-referencing foreign key that is required by EF Core convention (a non-nullable scalar property such as int, long, or Guid) is unconditionally unsatisfiable: the very first row of the entity has no earlier row of the same type to point to. AutoSeed's CycleResolver already rejects this shape at seeding time with a named UnresolvableCycleException; this analyzer reports the same, unconditionally unsatisfiable pattern at compile time.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        INamedTypeSymbol entityType = (INamedTypeSymbol)context.Symbol;

        if (entityType.TypeKind != TypeKind.Class || entityType.IsStatic)
        {
            return;
        }

        foreach (IPropertySymbol navigation in entityType.GetMembers().OfType<IPropertySymbol>())
        {
            if (!IsSelfReferenceNavigation(navigation, entityType))
            {
                continue;
            }

            string foreignKeyPropertyName = navigation.Name + "Id";
            IPropertySymbol? foreignKey = entityType.GetMembers(foreignKeyPropertyName).OfType<IPropertySymbol>().FirstOrDefault();

            if (foreignKey is null || !IsConventionMappedProperty(foreignKey) || !IsRequiredForeignKeyScalarType(foreignKey.Type))
            {
                continue;
            }

            Location location = foreignKey.Locations.FirstOrDefault(candidate => candidate.IsInSource)
                ?? entityType.Locations.First();

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, entityType.Name, foreignKey.Name, navigation.Name));
        }
    }

    private static bool IsSelfReferenceNavigation(IPropertySymbol property, INamedTypeSymbol containingType) =>
        IsConventionMappedProperty(property) && SymbolEqualityComparer.Default.Equals(property.Type, containingType);

    private static bool IsConventionMappedProperty(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsIndexer
        && property.DeclaredAccessibility != Accessibility.Private
        && property.GetMethod is not null
        && property.SetMethod is not null;

    private static bool IsRequiredForeignKeyScalarType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            return false;
        }

        if (type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
            or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64)
        {
            return true;
        }

        return type is { Name: "Guid", ContainingNamespace.Name: "System" };
    }
}
