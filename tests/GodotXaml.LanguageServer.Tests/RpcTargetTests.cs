using System.Reflection;
using System.Text.Json;
using StreamJsonRpc;

namespace GodotXaml.LanguageServer.Tests;

public sealed class RpcTargetTests
{
    private const string Uri = "untitled:Scene.xscn";

    private const string Scene = "<Control Name=\"Root\">\n  <Label Name=\"Title\" />\n</Control>";

    [Fact]
    public void EveryPublicMethodOfTheTargetIsAnRpcMethodOfTheInterface()
    {
        // The interface's generated shape is what registers the methods, so a method on the class alone is never called.
        InterfaceMapping map = typeof(LanguageServerTarget).GetInterfaceMap(typeof(ILanguageServerTarget));
        var declaredBy = map.TargetMethods.Zip(map.InterfaceMethods).ToDictionary(p => p.First, p => p.Second);
        MethodInfo[] methods =
        [
            .. typeof(LanguageServerTarget)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName),
        ];

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method =>
            {
                Assert.True(declaredBy.TryGetValue(method, out MethodInfo? declared), $"{method.Name} is not on ILanguageServerTarget");
                Assert.NotNull(declared.GetCustomAttribute<JsonRpcMethodAttribute>());
            }
        );
        Assert.All(map.InterfaceMethods, m => Assert.NotNull(m.GetCustomAttribute<JsonRpcMethodAttribute>()));
    }

    [Fact]
    public async Task ACancelledRequestEndsWithoutATransportWarningOrError()
    {
        StringWriter stderr = new();
        await using (ServerHarness server = new(stderr: stderr))
        {
            await server.InitializeAsync();
            await server.OpenAsync(Uri, 1, Scene);
            await server.NextAsync(server.Published, "the didOpen publish");

            using CancellationTokenSource cancel = new();
            Task<JsonElement> hover = server.RequestObjectAsync(
                "textDocument/hover",
                JsonSerializer.SerializeToElement(new { textDocument = new { uri = Uri }, position = new { line = 1, character = 4 } }),
                cancel.Token
            );
            // The request is on the wire, so the cancellation sends $/cancelRequest with its id.
            await cancel.CancelAsync();

            try
            {
                await server.Guard(hover, "the cancelled hover");
            }
            catch (OperationCanceledException) when (!server.HangGuard.IsCancellationRequested)
            {
                // Cancelled before the server answered: as good as an answer.
            }

            Assert.Equal(0, await server.ShutdownAndDrainAsync());
        }

        string log = stderr.ToString();
        Assert.DoesNotContain("jsonrpc Warning", log, StringComparison.Ordinal);
        Assert.DoesNotContain("jsonrpc Error", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", log, StringComparison.Ordinal);
    }
}
