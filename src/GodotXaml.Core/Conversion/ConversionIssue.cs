namespace GodotXaml.Core.Conversion;

/// <summary>How serious a conversion issue is.</summary>
public enum ConversionSeverity
{
    /// <summary>Something of the source is lost or kept in a form the target cannot load; the result must not be written.</summary>
    Error,

    /// <summary>The result loads as the same scene or resource, but something was guessed, dropped as unused or kept by name.</summary>
    Warning,
}

/// <summary>Something a conversion could not carry over as it stood.</summary>
/// <param name="Code">The stable code, one of <see cref="ConversionCodes"/>.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Message">What happened and what the result holds instead.</param>
/// <param name="Location">
/// Where in the source it lies: a section (<c>[sub_resource id="Gradient_t3x7r"]</c>) for a .tscn or .tres, an element
/// (<c>x:Resources "Panel"</c>, the root's tag) for an .xscn or .xres.
/// </param>
public sealed record ConversionIssue(string Code, ConversionSeverity Severity, string Message, string Location);
