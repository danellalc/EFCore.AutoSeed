namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when the <c>configure</c> callback passed to a seeding method refers to a type or
/// property that is not part of the <see cref="Microsoft.EntityFrameworkCore.DbContext"/>'s model,
/// typically a typo in an <c>Entity&lt;T&gt;()</c> or <c>Property(...)</c> call.
/// </summary>
public sealed class InvalidSeedConfigurationException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSeedConfigurationException"/> class for a
    /// type that is not an entity type in the model.
    /// </summary>
    /// <param name="typeName">The name of the CLR type passed to <c>Entity&lt;T&gt;()</c>.</param>
    public InvalidSeedConfigurationException(string typeName)
        : base($"'{typeName}' is not an entity type in this model.")
    {
        TypeName = typeName;
        PropertyName = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSeedConfigurationException"/> class for a
    /// property that is not mapped on an entity type that is otherwise part of the model.
    /// </summary>
    /// <param name="typeName">The name of the entity type that owns the property.</param>
    /// <param name="propertyName">The name of the property that is not mapped.</param>
    public InvalidSeedConfigurationException(string typeName, string propertyName)
        : base($"'{propertyName}' is not a property of '{typeName}' in this model.")
    {
        TypeName = typeName;
        PropertyName = propertyName;
    }

    /// <summary>
    /// The name of the CLR type <c>configure</c> referred to.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// The name of the property that is not mapped, or <see langword="null"/> when <see cref="TypeName"/>
    /// itself is not an entity type in the model.
    /// </summary>
    public string? PropertyName { get; }
}
