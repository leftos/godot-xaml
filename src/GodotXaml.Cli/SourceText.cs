using System.Text;

namespace GodotXaml.Cli;

/// <summary>Reads a file's text as strict UTF-8, so text in another encoding is refused rather than misread.</summary>
internal static class SourceText
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Reads a file's text, reporting why it could not be read.</summary>
    /// <param name="path">The path the output prints.</param>
    /// <param name="error">Where a failure is reported.</param>
    /// <returns>The text, or null when the file cannot be read or is not UTF-8 text.</returns>
    internal static string? Read(string path, TextWriter error)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            error.WriteLine($"{path}: cannot be read.");
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: access denied.");
            return null;
        }
        return Decode(bytes, path, error);
    }

    // The strict decoder rejects every UTF-16 and UTF-32 byte order mark, so only a UTF-8 one is stripped here. A NUL is
    // legal UTF-8 but no XML or Godot text holds one, so a file holding one is another encoding rather than text.
    private static string? Decode(byte[] bytes, string path, TextWriter error)
    {
        ReadOnlySpan<byte> text = bytes;
        if (text.StartsWith(Utf8Bom))
        {
            text = text[3..];
        }
        if (text.IndexOf((byte)0) >= 0)
        {
            error.WriteLine($"{path}: not UTF-8 text.");
            return null;
        }
        try
        {
            return Strict.GetString(text);
        }
        catch (DecoderFallbackException)
        {
            error.WriteLine($"{path}: not UTF-8 text.");
            return null;
        }
    }
}
