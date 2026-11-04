using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GodotXaml.Addon;

/// <summary>The result of <c>ping</c>, which a connection's first request must be.</summary>
/// <param name="Protocol">The protocol's version, <see cref="XscnPreviewProtocol.Version"/>.</param>
/// <param name="Pid">The editor's process id.</param>
/// <param name="Project">The project the editor has open, as a normalized full path.</param>
internal sealed record PreviewPingResult(int Protocol, int Pid, string Project);

/// <summary>A Control's rectangle in a <c>render</c> result, in the SubViewport's pixels.</summary>
/// <param name="Path">The node's path from the scene root.</param>
/// <param name="Class">The node's engine class.</param>
/// <param name="Kind">What the rectangle is of (<see cref="XscnPreviewRects.ControlKind"/>).</param>
/// <param name="X">The rectangle's left edge.</param>
/// <param name="Y">The rectangle's top edge.</param>
/// <param name="Width">The rectangle's width.</param>
/// <param name="Height">The rectangle's height.</param>
/// <param name="SpanStart">The UTF-16 offset in the text of the node's element.</param>
/// <param name="SpanLength">The element's length in UTF-16 units.</param>
internal sealed record PreviewRectInfo(
    string Path,
    string Class,
    string Kind,
    float X,
    float Y,
    float Width,
    float Height,
    int SpanStart,
    int SpanLength
);

/// <summary>A parse diagnostic in a <c>render</c> result.</summary>
/// <param name="Code">The diagnostic's GX code.</param>
/// <param name="Severity">Its severity's name.</param>
/// <param name="Message">Its message.</param>
/// <param name="Start">The UTF-16 offset of its span.</param>
/// <param name="Length">The span's length in UTF-16 units.</param>
internal sealed record PreviewDiagnosticInfo(string Code, string Severity, string Message, int Start, int Length);

/// <summary>A conversion issue in a <c>render</c> result.</summary>
/// <param name="Code">The issue's code.</param>
/// <param name="Severity">Its severity's name.</param>
/// <param name="Message">Its message.</param>
/// <param name="Location">Where it is.</param>
internal sealed record PreviewIssueInfo(string Code, string Severity, string Message, string Location);

/// <summary>The result of <c>render</c>.</summary>
/// <param name="Rects">The Controls' rectangles, back to front.</param>
/// <param name="Diagnostics">The parse's diagnostics.</param>
/// <param name="Issues">The conversion's issues.</param>
/// <param name="Loaded">Whether the scene was instantiated; false when the preview has no root.</param>
internal sealed record PreviewRenderResult(
    IReadOnlyList<PreviewRectInfo> Rects,
    IReadOnlyList<PreviewDiagnosticInfo> Diagnostics,
    IReadOnlyList<PreviewIssueInfo> Issues,
    bool Loaded
);

/// <summary>The discovery file's content: what a client needs to reach this editor's server.</summary>
/// <param name="Port">The server's loopback port.</param>
/// <param name="Token">The token a connection's first <c>ping</c> must carry.</param>
/// <param name="Pid">The editor's process id.</param>
/// <param name="Project">The project the editor has open, as a normalized full path.</param>
internal sealed record PreviewDiscoveryInfo(int Port, string Token, int Pid, string Project);

/// <summary>The parameters of <c>render</c>.</summary>
/// <param name="Document">The document's <c>res://</c> <c>.xscn</c> path.</param>
/// <param name="Text">The document's text.</param>
/// <param name="Width">The SubViewport's width, before the clamp.</param>
/// <param name="Height">The SubViewport's height, before the clamp.</param>
/// <param name="Scripts">Whether the request asks for the scene's scripts.</param>
internal sealed record PreviewRenderParams(string Document, string Text, int Width, int Height, bool Scripts);

/// <summary>A JSON-RPC 2.0 request or notification.</summary>
/// <param name="Id">The request's id, a string or a number; null for a notification.</param>
/// <param name="Method">The method's name.</param>
/// <param name="Params">The parameters; undefined when the message has none.</param>
internal sealed record PreviewRequest(JsonElement? Id, string Method, JsonElement Params);

/// <summary>The source-generated JSON metadata of the preview server's results and its discovery file.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PreviewPingResult))]
[JsonSerializable(typeof(PreviewRenderResult))]
[JsonSerializable(typeof(PreviewDiscoveryInfo))]
internal sealed partial class XscnPreviewJsonContext : JsonSerializerContext;

/// <summary>
/// The preview server's JSON-RPC 2.0 messages, hand-written with System.Text.Json: a request is parsed with duplicate
/// keys refused, so a second <c>token</c> cannot shadow the first, and a batch is refused.
/// </summary>
internal static class XscnPreviewProtocol
{
    /// <summary>The protocol's version, which <c>ping</c> answers.</summary>
    internal const int Version = 1;

    /// <summary>The method a connection opens with.</summary>
    internal const string PingMethod = "ping";

    /// <summary>The method that asks for a document's rectangles.</summary>
    internal const string RenderMethod = "render";

    /// <summary>The notification that cancels a queued request.</summary>
    internal const string CancelMethod = "$/cancelRequest";

    /// <summary>The body is not JSON, or repeats a key.</summary>
    internal const int ParseError = -32700;

    /// <summary>The body is no JSON-RPC 2.0 request; a batch included.</summary>
    internal const int InvalidRequest = -32600;

    /// <summary>The method is not one the server has.</summary>
    internal const int MethodNotFound = -32601;

    /// <summary>The parameters are missing or of the wrong type.</summary>
    internal const int InvalidParams = -32602;

    /// <summary>The render threw; the preview's reason is not sent.</summary>
    internal const int InternalError = -32603;

    /// <summary>The connection's first request is not a <c>ping</c> with the token; the connection is closed.</summary>
    internal const int NotAuthenticated = -32001;

    /// <summary>The <c>ping</c> names another project than the editor's.</summary>
    internal const int OtherProject = -32002;

    /// <summary>The document is no <c>res://</c> <c>.xscn</c> path without <c>..</c>, <c>\</c> or NUL.</summary>
    internal const int InvalidDocument = -32003;

    /// <summary>The server is stopping.</summary>
    internal const int Stopping = -32004;

    /// <summary>The request was cancelled, superseded by a newer one for its document, or over the queue's cap.</summary>
    internal const int Cancelled = -32800;

    private static readonly Dictionary<int, string> Messages = new()
    {
        [ParseError] = "Parse error: the body is not JSON, or repeats a key.",
        [InvalidRequest] = "Invalid request: not a JSON-RPC 2.0 request; batches are not accepted.",
        [MethodNotFound] = "Method not found.",
        [InvalidParams] = "Invalid params.",
        [InternalError] = "Internal error: the render failed.",
        [NotAuthenticated] = "Not authenticated: a connection opens with a ping that carries the discovery file's token.",
        [InvalidDocument] = "Invalid document path: a res:// .xscn path without .., \\ or NUL is expected.",
        [Stopping] = "The preview server is stopping.",
        [Cancelled] = "Request cancelled or superseded.",
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    /// <summary>Parses a body as a request or a notification.</summary>
    /// <param name="body">The frame's body.</param>
    /// <param name="error">The error code when the body is none; 0 otherwise.</param>
    /// <returns>The request, or null.</returns>
    internal static PreviewRequest? Parse(byte[] body, out int error)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body, DocumentOptions);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            error = ParseError;
            return null;
        }

        PreviewRequest? request = root.ValueKind == JsonValueKind.Object ? RequestOf(root) : null;
        error = request is null ? InvalidRequest : 0;
        return request;
    }

    /// <summary>A result response.</summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="id">The request's id.</param>
    /// <param name="value">The result.</param>
    /// <param name="info">The result's JSON metadata.</param>
    /// <returns>The response's body.</returns>
    internal static byte[] Result<T>(JsonElement id, T value, JsonTypeInfo<T> info) =>
        Envelope(
            id,
            writer =>
            {
                writer.WritePropertyName("result");
                JsonSerializer.Serialize(writer, value, info);
            }
        );

    /// <summary>An error response with the code's fixed message.</summary>
    /// <param name="id">The request's id, or null when it could not be read.</param>
    /// <param name="code">The error's code.</param>
    /// <returns>The response's body.</returns>
    internal static byte[] Error(JsonElement? id, int code) => Error(id, code, Messages[code]);

    /// <summary>An error response.</summary>
    /// <param name="id">The request's id, or null when it could not be read.</param>
    /// <param name="code">The error's code.</param>
    /// <param name="message">The error's message.</param>
    /// <returns>The response's body.</returns>
    internal static byte[] Error(JsonElement? id, int code, string message) =>
        Envelope(
            id,
            writer =>
            {
                writer.WriteStartObject("error");
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
            }
        );

    /// <summary>A string parameter.</summary>
    /// <param name="parameters">The request's parameters.</param>
    /// <param name="name">The parameter's name.</param>
    /// <returns>The value, or null when the parameter is missing or not a string.</returns>
    internal static string? StringParam(JsonElement parameters, string name) =>
        Param(parameters, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>The parameters of <c>render</c>.</summary>
    /// <param name="parameters">The request's parameters.</param>
    /// <returns>The parameters, or null when one is missing or of the wrong type.</returns>
    internal static PreviewRenderParams? RenderParams(JsonElement parameters)
    {
        string? document = StringParam(parameters, "document");
        string? text = StringParam(parameters, "text");
        int? width = IntParam(parameters, "width");
        int? height = IntParam(parameters, "height");
        bool? scripts = Param(parameters, "scripts") is { } value ? BoolOf(value) : false;
        return document is null || text is null || width is null || height is null || scripts is null
            ? null
            : new PreviewRenderParams(document, text, width.Value, height.Value, scripts.Value);
    }

    /// <summary>The id a <c>$/cancelRequest</c> names.</summary>
    /// <param name="parameters">The notification's parameters.</param>
    /// <returns>The id, or null when it names none.</returns>
    internal static JsonElement? CancelledId(JsonElement parameters) => Param(parameters, "id") is { } id && IsIdKind(id) ? id : null;

    /// <summary>Whether two ids are the same: of one kind with the same JSON text.</summary>
    /// <param name="left">One id.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they match.</returns>
    internal static bool SameId(JsonElement left, JsonElement right) =>
        left.ValueKind == right.ValueKind && string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static PreviewRequest? RequestOf(JsonElement root)
    {
        bool versioned = Param(root, "jsonrpc") is { ValueKind: JsonValueKind.String } version && version.ValueEquals("2.0");
        string? method = StringParam(root, "method");
        JsonElement? id = Param(root, "id");
        if (!versioned || method is null || (id is { } given && !IsIdKind(given)))
        {
            return null;
        }

        return new PreviewRequest(id, method, Param(root, "params") ?? default);
    }

    private static bool IsIdKind(JsonElement id) => id.ValueKind is JsonValueKind.String or JsonValueKind.Number;

    private static JsonElement? Param(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out JsonElement value) ? value : null;

    private static int? IntParam(JsonElement parameters, string name) =>
        Param(parameters, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out int number) ? number : null;

    private static bool? BoolOf(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static byte[] Envelope(JsonElement? id, Action<Utf8JsonWriter> body)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (id is { } given)
            {
                given.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            body(writer);
            writer.WriteEndObject();
        }

        return output.WrittenSpan.ToArray();
    }
}
