# LSP libraries for the .xscn language server (read 2026-11-02)

Facts marked **[verified]** were read this session from the named API or page; facts marked **[unverified]** are from memory and need a check before they go into a decision record.

## 1. .NET server libraries

| Library | Latest on nuget.org | License | TFMs | Activity |
|---|---|---|---|---|
| OmniSharp.Extensions.LanguageServer | 0.19.11, 2026-10-07 (stable). A 1.0.0 also exists but shows publish date 1900-01-01, which is how nuget.org marks an unlisted package | MIT (repo) | 1.0.0: net10.0, net8.0, netstandard2.0/2.1 | Repo pushed 2026-11-02; releases v0.19.10 (2026-06-30) and v0.19.11 (2026-10-07) [verified] |
| StreamJsonRpc | 2.26.150, 2026-10-19 | MIT | net8.0, net9.0, netstandard2.0/2.1 | Repo pushed 2026-11-03 [verified] |
| Microsoft.VisualStudio.LanguageServer.Protocol | 17.2.8, 2022-05-31 | no SPDX expression (MS EULA) | netstandard2.0 | Abandoned on nuget.org for four years [verified] |
| Microsoft.CommonLanguageServerProtocol.Framework (CLaSP) | only 5.0.0-1.25277.114 (prerelease), 2025-06-06 | MIT | netstandard2.0 | One prerelease drop; Roslyn and Razor consume it from source/dnceng feeds [verified version; feed usage unverified] |
| EmmyLua.LanguageServer.Framework | 0.9.2, 2026-04-15 | MIT | net8.0, net9.0 | pre-1.0, single-project user (EmmyLua) [verified version] |

- OmniSharp 1.0 (master README and `docs/migration-v1.md`) drops MediatR and Newtonsoft.Json for System.Text.Json and its own dispatch contracts [verified]. That removes the two main trimming/AOT blockers of 0.19.x; whether 1.0 is actually trim/AOT-clean (source-generated JSON contexts, no reflection-based DI scanning) is **not verified**. 0.19.x relies on Newtonsoft and reflection, so it is not AOT-compatible [unverified, widely reported]. Users: OmniSharp/Roslyn-era C# tooling, Bicep, PowerShell Editor Services, Unity and others [unverified].
- StreamJsonRpc plus types: this is how Roslyn and Razor do it, with CLaSP on top and LSP types generated in-repo. CLaSP is not usable by third parties in practice (one prerelease, no stable). Writing our own types for the handful of messages we need is a few hundred lines; StreamJsonRpc's System.Text.Json formatter has AOT/trim annotations in recent versions [unverified for 2.26].
- Microsoft.VisualStudio.LanguageServer.Protocol: stale (LSP ~3.16 era), do not use.
- LSP 3.17 coverage needed (completion, hover, publishDiagnostics, pull diagnostics `textDocument/diagnostic`, formatting, incremental sync): OmniSharp implements all of these including pull diagnostics [unverified per-feature; README claims goal of full conformance]. Hand-rolled types over StreamJsonRpc support whatever we write. Type generation from the LSP `metaModel.json` (in microsoft/vscode-languageserver-node, `protocol/metaModel.json`, types release 3.18.5 on 2026-10-22 [verified tag]) is an option for a hand-rolled route.

## 2. VS Code side

- `vscode-languageclient` latest on npm: 11.0.0 [verified, registry read 2026-11-02]. Repo microsoft/vscode-languageserver-node active (pushed 2026-10-31) [verified].
- Guide: https://code.visualstudio.com/api/language-extensions/language-server-extension-guide [verified].
- Launch options [unverified, from docs memory]: (a) ship a framework-dependent dll and call the .NET Install Tool (`ms-dotnettools.vscode-dotnet-runtime`, command `dotnet.acquire`) as an extension dependency to get a runtime; used by C# Dev Kit and others; small VSIX (~1-2 MB). (b) platform-specific VSIX (`vsce package --target win32-x64 linux-x64 darwin-arm64 ...`, https://code.visualstudio.com/api/working-with-extensions/publishing-extension#platformspecific-extensions) each with a self-contained or NativeAOT server; no runtime download, one VSIX per target, tens of MB self-contained or ~10-20 MB AOT.

## 3. Visual Studio side

- Legacy: `Microsoft.VisualStudio.LanguageServer.Client` (`ILanguageClient`, VSSDK/MEF, in-proc). Latest 17.14.60, 2025-05-14, net472/netstandard2.0 [verified]. Still works; no new feature investment [unverified].
- New: VisualStudio.Extensibility `LanguageServerProvider` (https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/language-server-provider/language-server-provider) [verified page]: override `LanguageServerProviderConfiguration` (document filters) and `CreateServerConnectionAsync` returning an `IDuplexPipe`, so the server can be any process the extension starts (stdio or named pipe), any framework. SDK latest Microsoft.VisualStudio.Extensibility.Sdk 17.14.40608, 2025-10-02 [verified]. Out of preview since VS 17.12 / SDK 17.12 [unverified]; runs out-of-proc, so also supported in VS 2026 [unverified].
- The extension must ship the server itself (framework-dependent needs a .NET runtime on the machine; VS installs one but not guaranteed to match ours [unverified]); a self-contained win-x64/arm64 build avoids the dependency.

## 4. Recommendation

- **Library**: StreamJsonRpc plus our own hand-written LSP types for the ~10 messages we need, with System.Text.Json source generation. Worst case: we write and maintain types; small surface, no abandonment risk (Microsoft's core VS dependency), AOT-able.
  - Alternative: OmniSharp 1.0 once a listed stable 1.0 is on nuget.org. Worst case: single-maintainer cadence (stalled 2023-2025 before the 0.19.10/11 releases), AOT not proven, larger dependency tree.
  - Avoid: Microsoft.VisualStudio.LanguageServer.Protocol (dead since 2022), CLaSP (prerelease only).
- **VS Code**: platform-specific VSIXes with a NativeAOT (or self-contained) server per target. Worst case: build matrix of 5-6 targets and larger downloads. Fallback: framework-dependent dll + .NET Install Tool, worst case a first-run runtime download that can fail offline.
- **Visual Studio**: VisualStudio.Extensibility `LanguageServerProvider` launching the same win-x64/arm64 server over stdio. Worst case: requires VS 17.12+ and out-of-proc extensibility; legacy ILanguageClient remains a fallback.

## References
- https://api.nuget.org/v3/registration5-gz-semver2/{package}/index.json (versions/dates/TFMs, read 2026-11-02)
- https://github.com/OmniSharp/csharp-language-server-protocol (README, docs/migration-v1.md, releases)
- https://github.com/microsoft/vs-streamjsonrpc/releases/tag/v2.26.150
- https://github.com/microsoft/vscode-languageserver-node
- https://www.npmjs.com/package/vscode-languageclient
- https://code.visualstudio.com/api/language-extensions/language-server-extension-guide
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/language-server-provider/language-server-provider
