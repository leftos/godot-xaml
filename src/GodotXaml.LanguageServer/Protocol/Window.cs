namespace GodotXaml.LanguageServer.Protocol;

/// <summary>How a message to the user or the log is meant.</summary>
internal enum MessageType
{
    /// <summary>An error.</summary>
    Error = 1,

    /// <summary>A warning.</summary>
    Warning = 2,

    /// <summary>Information.</summary>
    Info = 3,

    /// <summary>A log line.</summary>
    Log = 4,
}

/// <summary>The <c>window/logMessage</c> notification's parameters.</summary>
/// <param name="Type">How the message is meant.</param>
/// <param name="Message">The message.</param>
internal sealed record LogMessageParams(MessageType Type, string Message);

/// <summary>The <c>window/showMessage</c> notification's parameters.</summary>
/// <param name="Type">How the message is meant.</param>
/// <param name="Message">The message.</param>
internal sealed record ShowMessageParams(MessageType Type, string Message);
