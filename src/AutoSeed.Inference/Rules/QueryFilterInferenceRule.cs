using System.Linq.Expressions;
using System.Reflection;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Biases a property referenced by its entity type's global query filter toward the value that
/// passes the filter, so that seeded data is not mostly invisible behind a soft-delete or
/// similar filter. Recognizes the common single-property shapes only: <c>e =&gt; !e.IsDeleted</c>,
/// <c>e =&gt; e.IsActive</c>, <c>e =&gt; e.Flag == true</c> or <c>== false</c>, and
/// <c>e =&gt; e.DeletedAt == null</c>. A filter of any other shape is left alone.
/// </summary>
public sealed class QueryFilterInferenceRule : IPropertyInferenceRule
{
    private const double PassesFilterProbability = 0.9;

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        !property.IsForeignKey() && FindFilteredValue(property) is not null;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        FilteredPropertyValue filtered = FindFilteredValue(property)
            ?? throw new InvalidOperationException($"'{property.Name}' is not governed by a recognized query filter.");

        bool passesFilter = random.NextDouble() < PassesFilterProbability;
        return passesFilter ? filtered.PassingValue : filtered.FailingValue;
    }

    private static FilteredPropertyValue? FindFilteredValue(IProperty property)
    {
        if (property.DeclaringType is not IEntityType entityType)
        {
            return null;
        }

        LambdaExpression? filter = GetQueryFilter(entityType);
        return filter is null ? null : ParseSimpleFilter(filter, property);
    }

    /// <summary>
    /// EF Core 10 added support for multiple, named query filters per entity type and obsoleted
    /// the single-filter <c>GetQueryFilter()</c> in favor of <c>GetDeclaredQueryFilters()</c>; EF
    /// Core 9 (this library's other supported major) only has the former. Only the classic,
    /// single unnamed filter is recognized here either way.
    /// </summary>
    private static LambdaExpression? GetQueryFilter(IEntityType entityType)
    {
#if NET10_0_OR_GREATER
        return entityType.GetDeclaredQueryFilters().FirstOrDefault()?.Expression;
#else
        return entityType.GetQueryFilter();
#endif
    }

    private static FilteredPropertyValue? ParseSimpleFilter(LambdaExpression filter, IProperty property)
    {
        ParameterExpression parameter = filter.Parameters[0];
        Expression body = StripConvert(filter.Body);

        if (body is UnaryExpression { NodeType: ExpressionType.Not } not
            && TryMatchProperty(not.Operand, parameter, property))
        {
            return new FilteredPropertyValue(false, true);
        }

        if (body is BinaryExpression { NodeType: ExpressionType.Equal } equal
            && TryMatchProperty(equal.Left, parameter, property)
            && equal.Right is ConstantExpression constant)
        {
            return constant.Value switch
            {
                bool boolValue => new FilteredPropertyValue(boolValue, !boolValue),
                null => new FilteredPropertyValue(null, NonNullPlaceholder(property)),
                _ => null,
            };
        }

        if (TryMatchProperty(body, parameter, property))
        {
            return new FilteredPropertyValue(true, false);
        }

        return null;
    }

    private static bool TryMatchProperty(Expression expression, ParameterExpression parameter, IProperty property) =>
        StripConvert(expression) is MemberExpression { Member: PropertyInfo propertyInfo } member
        && member.Expression == parameter
        && propertyInfo.Name == property.Name;

    private static Expression StripConvert(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert } unary ? unary.Operand : expression;

    private static object NonNullPlaceholder(IProperty property)
    {
        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        return clrType == typeof(DateTime) ? DateTime.UnixEpoch : Activator.CreateInstance(clrType)!;
    }

    private sealed record FilteredPropertyValue(object? PassingValue, object? FailingValue);
}
