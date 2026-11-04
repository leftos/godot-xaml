using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GodotXaml.Addon;

/// <summary>
/// The preview server's <c>Content-Length</c> framing, as the language server protocol's: a header block of
/// <c>Name: value</c> lines ending in a blank line, then the body. A header block over <see cref="MaxHeaderBytes"/>, a line
/// that is no header (an HTTP request line), a <c>Content-Length</c> that is missing, repeated, not all digits or over the
/// cap is malformed, and the connection that sent it is closed.
/// </summary>
internal static class XscnPreviewFraming
{
    /// <summary>The most bytes a header block may take, its closing blank line included.</summary>
    internal const int MaxHeaderBytes = 1024;

    /// <summary>The largest body a connection may send before it has authenticated.</summary>
    internal const int MaxUnauthenticatedBody = 4 * 1024;

    /// <summary>The largest body an authenticated connection may send.</summary>
    internal const int MaxAuthenticatedBody = 16 * 1024 * 1024;

    private const string LengthHeader = "Content-Length";

    // Nine digits keep the parse inside an int; the cap is far below.
    private const int MaxLengthDigits = 9;

    /// <summary>The frame of a body: its header block, then the body.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The frame's bytes.</returns>
    internal static byte[] Frame(ReadOnlySpan<byte> body)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{LengthHeader}: {body.Length}\r\n\r\n"));
        byte[] frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame.AsSpan(header.Length));
        return frame;
    }

    /// <summary>Writes one frame in a single write, then flushes.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="body">The body.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The write.</returns>
    internal static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Frame(body.Span), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The body length a header block names.</summary>
    /// <param name="header">The header block without its closing blank line.</param>
    /// <param name="maxBody">The largest body the connection may send now.</param>
    /// <returns>The body's length in bytes.</returns>
    /// <exception cref="InvalidDataException">The header block is malformed or names a body over the cap.</exception>
    internal static int BodyLength(ReadOnlySpan<byte> header, int maxBody)
    {
        int? length = null;
        foreach (string line in Encoding.ASCII.GetString(header).Split("\r\n"))
        {
            if (!IsLengthHeader(line, out string value))
            {
                continue;
            }

            length = length is null ? LengthOf(value, maxBody) : throw Malformed("a second Content-Length header");
        }

        return length ?? throw Malformed("no Content-Length header");
    }

    /// <summary>The exception a malformed frame raises.</summary>
    /// <param name="reason">What is wrong with it.</param>
    /// <returns>The exception.</returns>
    internal static InvalidDataException Malformed(string reason) => new($"A malformed preview frame: {reason}.");

    // Whether the line is the Content-Length header, and its value; a line that is no "Name: value" header throws.
    private static bool IsLengthHeader(string line, out string value)
    {
        int colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !line.AsSpan(0, colon).ContainsOnlyHeaderNameChars() || !line.AsSpan(colon + 1).IsPrintableAscii())
        {
            throw Malformed("a line that is no header");
        }

        value = line[(colon + 1)..].Trim(' ');
        return line.AsSpan(0, colon).Equals(LengthHeader, StringComparison.OrdinalIgnoreCase);
    }

    private static int LengthOf(string value, int maxBody)
    {
        if (value.Length == 0 || value.Length > MaxLengthDigits || !value.AsSpan().ContainsOnlyAsciiDigits())
        {
            throw Malformed("a Content-Length that is not a number");
        }

        int length = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        return length <= maxBody ? length : throw Malformed($"a body of {length} bytes, over the cap of {maxBody}");
    }

    private static bool ContainsOnlyHeaderNameChars(this ReadOnlySpan<char> name) => !name.ContainsAnyExcept(HeaderNameChars);

    private static bool ContainsOnlyAsciiDigits(this ReadOnlySpan<char> text) => !text.ContainsAnyExceptInRange('0', '9');

    private static bool IsPrintableAscii(this ReadOnlySpan<char> text) => !text.ContainsAnyExceptInRange(' ', '~');

    private static readonly System.Buffers.SearchValues<char> HeaderNameChars = System.Buffers.SearchValues.Create(
        "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
    );
}

/// <summary>Reads the frames of one connection, buffering what a read brings past a frame for the next.</summary>
/// <param name="stream">The connection's stream.</param>
internal sealed class XscnPreviewFrameReader(Stream stream)
{
    private static readonly byte[] BlankLine = "\r\n\r\n"u8.ToArray();

    private readonly byte[] buffer = new byte[4096];

    private int position;

    private int filled;

    /// <summary>Reads the next frame's body.</summary>
    /// <param name="maxBody">The largest body the connection may send now.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The body, or null when the stream ended cleanly before a frame began.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside the body.</exception>
    internal async Task<byte[]?> ReadAsync(int maxBody, CancellationToken cancellationToken)
    {
        byte[]? header = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        byte[] body = new byte[XscnPreviewFraming.BodyLength(header, maxBody)];
        int taken = Math.Min(body.Length, filled - position);
        Array.Copy(buffer, position, body, 0, taken);
        position += taken;
        await stream.ReadExactlyAsync(body.AsMemory(taken), cancellationToken).ConfigureAwait(false);
        return body;
    }

    // The header block without its closing blank line, or null at a clean end before its first byte.
    private async Task<byte[]?> ReadHeaderAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[XscnPreviewFraming.MaxHeaderBytes];
        int count = 0;
        while (count < BlankLine.Length || !header.AsSpan(count - BlankLine.Length, BlankLine.Length).SequenceEqual(BlankLine))
        {
            if (position == filled && !await FillAsync(cancellationToken).ConfigureAwait(false))
            {
                return count == 0 ? null : throw XscnPreviewFraming.Malformed("the stream ended inside a header");
            }

            header[count < header.Length ? count : throw XscnPreviewFraming.Malformed("a header block over 1 KiB")] = buffer[position];
            count++;
            position++;
        }

        return header[..(count - BlankLine.Length)];
    }

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        filled = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        position = 0;
        return filled > 0;
    }
}
