using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer.Tests;

public sealed class DocumentSyncTests
{
    private const string Uri = "untitled:Scene.xscn";

    // A Label without a Name: GX0104 on the second line.
    private const string Broken = "<Control Name=\"Root\">\n  <Label />\n</Control>";

    private const string Clean = "<Control Name=\"Root\">\n  <Label Name=\"Title\" />\n</Control>";

    private const string Href = "https://github.com/leftos/godot-xaml/blob/main/docs/format/diagnostics.md#gx0104-node-without-a-name";

    [Fact]
    public async Task DidOpenPublishesTheErrorAtOnceWithItsCodeRangeAndLink()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();

        await server.OpenAsync(Uri, 1, Broken);
        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the didOpen publish");

        XscnDiagnostic expected = Assert.Single(XscnParser.Parse(Broken, XscnDocumentKind.Scene, GodotMetadata.LoadBundled("4.7")).Diagnostics);
        Assert.Equal(XscnCodes.MissingName, expected.Code);
        LineMap lines = new(Broken);
        LinePosition start = lines.ZeroBased(expected.Span.Start);
        LinePosition end = lines.ZeroBased(expected.Span.End);

        Assert.Equal(Uri, published.Uri);
        Assert.Equal(1, published.Version);
        LspDiagnostic diagnostic = Assert.Single(published.Diagnostics);
        Assert.Equal("GX0104", diagnostic.Code);
        Assert.Equal(expected.Severity == XscnSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("godot-xaml", diagnostic.Source);
        Assert.Equal(expected.Message, diagnostic.Message);
        Assert.Equal(Href, diagnostic.CodeDescription?.Href);
        Assert.Equal(new LspRange(new LspPosition(start.Line, start.Column), new LspPosition(end.Line, end.Column)), diagnostic.Range);
        Assert.False(server.Scheduler.Requested.TryRead(out _), "didOpen waited on the debounce");
    }

    [Fact]
    public async Task DidChangeToACleanTextPublishesAnEmptyListOnceTheDebounceElapses()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");

        await server.ChangeAsync(Uri, 2, Clean);
        PendingDelay delay = await server.NextAsync(server.Scheduler.Requested, "the change's debounce");
        Assert.Equal(TimeSpan.FromMilliseconds(250), delay.Delay);
        Assert.False(server.Published.TryRead(out _), "the change published before its debounce elapsed");
        delay.Release();

        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the change's publish");
        Assert.Equal(2, published.Version);
        Assert.Empty(published.Diagnostics);
    }

    [Fact]
    public async Task TwoChangesBeforeAReleasePublishOnceForTheSecondVersion()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");

        await server.ChangeAsync(Uri, 2, Clean);
        PendingDelay first = await server.NextAsync(server.Scheduler.Requested, "the first change's debounce");
        await server.ChangeAsync(Uri, 3, Broken);
        PendingDelay second = await server.NextAsync(server.Scheduler.Requested, "the second change's debounce");
        Assert.True(first.Task.IsCanceled, "the second change did not cancel the first one's debounce");
        first.Release();
        second.Release();

        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the second change's publish");
        Assert.Equal(3, published.Version);
        Assert.Single(published.Diagnostics);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task AStaleParseNeverPublishes()
    {
        const string Stale = "<Control Name=\"Root\">\n  <Label Name=\"Stale\" />\n</Control>";
        using ManualResetEventSlim release = new();
        TaskCompletionSource staleStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerSeams seams = ServerSeams.Default with
        {
            Parse = (text, kind, metadata) =>
            {
                if (text == Stale)
                {
                    staleStarted.TrySetResult();
                    release.Wait(TimeSpan.FromMinutes(2));
                }
                return XscnParser.Parse(text, kind, metadata);
            },
        };
        await using ServerHarness server = new(seams);
        try
        {
            await server.InitializeAsync();
            await server.OpenAsync(Uri, 1, Broken);
            await server.NextAsync(server.Published, "the didOpen publish");

            await server.ChangeAsync(Uri, 2, Stale);
            (await server.NextAsync(server.Scheduler.Requested, "the stale change's debounce")).Release();
            await server.Guard(staleStarted.Task, "the stale parse to start");

            await server.ChangeAsync(Uri, 3, Clean);
            (await server.NextAsync(server.Scheduler.Requested, "the newer change's debounce")).Release();
            PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the newer change's publish");
            Assert.Equal(3, published.Version);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task DidCloseCancelsThePendingChangePublishesAnEmptyListAndDropsTheBuffer()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");
        await server.ChangeAsync(Uri, 2, Broken);
        PendingDelay pending = await server.NextAsync(server.Scheduler.Requested, "the change's debounce");

        await server.CloseAsync(Uri);
        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the didClose publish");
        Assert.Equal(Uri, published.Uri);
        Assert.Empty(published.Diagnostics);
        Assert.True(pending.Task.IsCanceled, "didClose did not cancel the pending change");
        pending.Release();

        // A change to a closed document has no buffer to change.
        await server.ChangeAsync(Uri, 3, Broken);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
        Assert.False(server.Scheduler.Requested.TryRead(out _), "a change to a closed document was scheduled");
    }

    [Fact]
    public async Task ShutdownWithADebouncePendingEndsCleanlyAndPublishesNothingForIt()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");
        await server.ChangeAsync(Uri, 2, Clean);
        PendingDelay pending = await server.NextAsync(server.Scheduler.Requested, "the change's debounce");

        Assert.Equal(0, await server.ShutdownAndDrainAsync());

        Assert.True(pending.Task.IsCanceled, "shutdown did not cancel the pending debounce");
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task ACancelledInlineDebounceEndsCleanlyOnChangeAndOnShutdown()
    {
        await using ServerHarness server = new(inlineDelays: true);
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");
        await server.ChangeAsync(Uri, 2, Clean);
        PendingDelay first = await server.NextAsync(server.Scheduler.Requested, "the first change's debounce");
        await server.ChangeAsync(Uri, 3, Broken);
        PendingDelay second = await server.NextAsync(server.Scheduler.Requested, "the second change's debounce");

        Assert.Equal(0, await server.ShutdownAndDrainAsync());

        Assert.True(first.Task.IsCanceled && second.Task.IsCanceled, "a pending debounce was not cancelled");
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task DidCloseDuringARunningParseIsTheLastPublishForTheDocument()
    {
        const string Slow = "<Control Name=\"Root\">\n  <Label Name=\"Slow\" />\n</Control>";
        using ManualResetEventSlim release = new();
        TaskCompletionSource slowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerSeams seams = ServerSeams.Default with
        {
            Parse = (text, kind, metadata) =>
            {
                if (text == Slow)
                {
                    slowStarted.TrySetResult();
                    release.Wait(TimeSpan.FromMinutes(2));
                }
                return XscnParser.Parse(text, kind, metadata);
            },
        };
        await using ServerHarness server = new(seams);
        try
        {
            await server.InitializeAsync();
            await server.OpenAsync(Uri, 1, Broken);
            await server.NextAsync(server.Published, "the didOpen publish");
            await server.ChangeAsync(Uri, 2, Slow);
            (await server.NextAsync(server.Scheduler.Requested, "the change's debounce")).Release();
            await server.Guard(slowStarted.Task, "the parse to start");

            await server.CloseAsync(Uri);
            PublishDiagnosticsParams closed = await server.NextAsync(server.Published, "the didClose publish");
            Assert.Empty(closed.Diagnostics);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task ACancellationTheDocumentDidNotAskForIsAFailedCheck()
    {
        ServerSeams seams = ServerSeams.Default with { Parse = (_, _, _) => throw new OperationCanceledException("not the document's") };
        await using ServerHarness server = new(seams);
        await server.InitializeAsync();

        await server.OpenAsync(Uri, 1, Broken);

        // Wait for the failure before the shutdown, whose own cancellation would otherwise stand in for it.
        LogMessageParams logged;
        do
        {
            logged = await server.NextAsync(server.Logged, "the failed check's log");
        } while (logged.Type != MessageType.Error);
        Assert.Contains(Uri, logged.Message, StringComparison.Ordinal);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }
}
