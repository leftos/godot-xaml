using System.Text.Json;
using System.Text.Json.Serialization;
using StreamJsonRpc.Protocol;

namespace GodotXaml.LanguageServer.Protocol;

/// <summary>The source-generated JSON metadata of every message type the server sends or reads.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializedParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(TextEdit[]))]
[JsonSerializable(typeof(HoverParams))]
[JsonSerializable(typeof(Hover))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(LogMessageParams))]
[JsonSerializable(typeof(ShowMessageParams))]
[JsonSerializable(typeof(JsonElement))]
// StreamJsonRpc writes an error reply's data, for any exception but a LocalRpcException, through these options.
[JsonSerializable(typeof(CommonErrorData))]
internal sealed partial class LspJsonContext : JsonSerializerContext;
