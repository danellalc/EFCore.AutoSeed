namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Base type for every exception thrown by EFCore.AutoSeed. Never thrown directly; always one of
/// its derived, named types.
/// </summary>
public abstract class AutoSeedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AutoSeedException"/> class.
    /// </summary>
    /// <param name="message">A message that names the model elements involved and explains why the operation failed.</param>
    protected AutoSeedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoSeedException"/> class with an underlying cause.
    /// </summary>
    /// <param name="message">A message that names the model elements involved and explains why the operation failed.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    protected AutoSeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
