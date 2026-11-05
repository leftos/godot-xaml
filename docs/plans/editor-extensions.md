# Editor extensions

The design of the VS Code extension (GXAML-15) and the Visual Studio extension (GXAML-16), both clients of the language server (`src/GodotXaml.LanguageServer`, stdio, no arguments, metadata embedded), and the host of the Visual Studio preview pane (GXAML-17). The settled choices are the owner's; the open ones are asked before the item that needs them is briefed.

## Settled

- **Layout.** Both extensions live in this repo, under `editors/vscode/` and `editors/visualstudio/`, beside the server, so they version and build with it and their packaging reads the release's server zips directly. The repo's gates (`run.ps1`, the hooks, docs lint) learn each extension's toolchain when its skeleton lands.
- **VS Code: the server executable.** One VSIX per platform (`win32-x64`, `win32-arm64`, `linux-x64`, `linux-arm64`, `darwin-x64`, `darwin-arm64`, through `vsce package --target`), each embedding the executable from that platform's release zip, as the language server's packaging decision says ("bundled per platform"). A `godotXaml.server.path` setting overrides the embedded executable. The VSIXs are built after the release's zips are attached, and the macOS executable keeps its executable bit and its notarisation inside the VSIX.
- **VS Code: publishing.** The VS Code Marketplace and Open VSX, from a tag-gated release job whose publisher tokens live in a protected environment, as the `macos-signing` secrets do.
- **VS Code: language registration.** `.xscn` and `.xres` are their own languages, `xscn` and `xres`, coloured by VS Code's built-in XML TextMate grammar, with XML's comment and bracket configuration. Only this extension's language server attaches to them.
- **VS Code: toolchain.** TypeScript with `vscode-languageclient`, under the user-level Node/TypeScript conventions (pnpm, oxlint, oxfmt).
- **Versions.** Each extension's version is the release's (`VersionPrefix` in `Directory.Build.props`), bumped by `tools/pack-release.ps1` with the other version homes, and its packages are built and published by the release that attaches the server zips. An extension-only fix ships in a godot-xaml release.
- **VS Code: settings.** `godotXaml.server.path` (another executable in place of the embedded one) and `godotXaml.trace.server` (the language client's standard message trace), both read by the client alone; the server still takes no arguments and no initialization options.
- **VS Code: branch.** The extension is built on `feat/vscode-extension`, with a draft PR into `main`, and merges once its per-platform packaging is proven.

- **Visual Studio: extension model.** One VSIX in the hybrid model: VisualStudio.Extensibility's APIs with in-process hosting (`VssdkCompatibleExtension`, .NET Framework 4.7.2, `RequiresInProcessHosting`, manifest type `VSSDK+VisualStudio.Extensibility`), Microsoft's documented route for the new SDK's gaps. `LanguageServerProvider` launches the server (it is still experimental, `VSEXTPREVIEW_LSP`, so its use is suppressed by name), a pkgdef-registered TextMate grammar colours the files, and GXAML-17 may host a classic WPF tool window for the live preview image and its click positions, which Remote UI cannot report.
- **Visual Studio: targets.** `InstallationTarget` `[17.14,)` for amd64 and for arm64, so Visual Studio 2022 17.14 and later and Visual Studio 2026 both install it; the VSIX embeds the win-x64 and win-arm64 server executables and picks one by the OS architecture at run time.
- **Visual Studio: branch.** The extension is built on `feat/visual-studio-extension`, with a draft PR into `main`, and merges once its packaging is proven.

## Open

- Visual Studio: the content type (its own, based on XML or on the language-server base document type) and how its colour coexists with the built-in XML editor; its Marketplace publishing (`VsixPublisher.exe`, trusted publishing from CI).
