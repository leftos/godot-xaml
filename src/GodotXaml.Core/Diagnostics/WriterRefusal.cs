namespace GodotXaml.Core.Diagnostics;

/// <summary>
/// Tells the core refusing a document it cannot read, convert or write (a <see cref="NotSupportedException"/>, or an
/// <see cref="ArgumentException"/> other than <see cref="ArgumentNullException"/>) from a bug, for the hosts that report a
/// refusal on the document's own line and let a bug surface.
/// </summary>
public static class WriterRefusal
{
    /// <summary>Whether an exception is the core refusing a document rather than a bug.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>Whether it is a refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static bool Is(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is NotSupportedException or (ArgumentException and not ArgumentNullException);
    }
}
