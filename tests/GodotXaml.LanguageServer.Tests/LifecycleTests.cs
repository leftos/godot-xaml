using System.Text.Json;
using StreamJsonRpc;

namespace GodotXaml.LanguageServer.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task ARoundTripAdvertisesOnlyFullTextSyncAndEndsWithZero()
    {
        await using ServerHarness server = new();

        JsonElement result = await server.InitializeRawAsync();

        JsonElement capabilities = result.GetProperty("capabilities");
        Assert.Equal(["textDocumentSync", "documentFormattingProvider"], capabilities.EnumerateObject().Select(p => p.Name));
        JsonElement sync = capabilities.GetProperty("textDocumentSync");
        Assert.Equal(["change", "openClose"], sync.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(sync.GetProperty("openClose").GetBoolean());
        Assert.Equal(1, sync.GetProperty("change").GetInt32());
        JsonElement serverInfo = result.GetProperty("serverInfo");
        Assert.Equal("godot-xaml", serverInfo.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(serverInfo.GetProperty("version").GetString()));

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task TheTypedInitializeResultCarriesTheSyncKind()
    {
        await using ServerHarness server = new();

        Protocol.InitializeResult result = await server.InitializeAsync();

        Assert.True(result.Capabilities.TextDocumentSync.OpenClose);
        Assert.Equal(Protocol.TextDocumentSyncKind.Full, result.Capabilities.TextDocumentSync.Change);
        Assert.Equal("godot-xaml", result.ServerInfo.Name);
    }

    [Fact]
    public async Task InitializeWithNullParamsGetsAnErrorReply()
    {
        await using ServerHarness server = new();

        // params [null] reaches the method, which throws; the reply carries StreamJsonRpc's error data.
        await Assert.ThrowsAsync<RemoteInvocationException>(() => server.RequestAsync("initialize", [null]));
    }

    [Fact]
    public async Task InitializedWithAnEmptyObjectIsAcceptedAndLogsTheStart()
    {
        await using ServerHarness server = new();

        await server.InitializeAsync();

        Protocol.LogMessageParams logged = await server.NextAsync(server.Logged, "the started log");
        Assert.Equal(Protocol.MessageType.Info, logged.Type);
        Assert.Contains("started", logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestBeforeInitializeFailsAsNotInitialized()
    {
        await using ServerHarness server = new();

        RemoteInvocationException error = await Assert.ThrowsAsync<RemoteInvocationException>(server.ShutdownAsync);

        Assert.Equal(-32002, error.ErrorCode);
    }

    [Fact]
    public async Task ARequestAfterShutdownFailsAsInvalid()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.ShutdownAsync();

        RemoteInvocationException error = await Assert.ThrowsAsync<RemoteInvocationException>(server.ShutdownAsync);

        Assert.Equal(-32600, error.ErrorCode);
    }

    [Fact]
    public async Task ExitWithoutShutdownEndsWithOne()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();

        Assert.Equal(1, await server.ExitAsync());
    }

    [Fact]
    public async Task ExitAfterShutdownEndsWithZero()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.ShutdownAsync();

        Assert.Equal(0, await server.ExitAsync());
    }
}
