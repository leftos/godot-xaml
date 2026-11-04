# Editor extensions

The design of the VS Code extension (GXAML-15) and the Visual Studio extension (GXAML-16), both clients of the language server (`src/GodotXaml.LanguageServer`, stdio, no arguments, metadata embedded), and the host of the Visual Studio preview pane (GXAML-17). The settled choices are the owner's; the open ones are asked before the item that needs them is briefed.

## Settled

- **Layout.** Both extensions live in this repo, under `editors/vscode/` and `editors/visualstudio/`, beside the server, so they version and build with it and their packaging reads the release's server zips directly. The repo's gates (`run.ps1`, the hooks, docs lint) learn each extension's toolchain when its skeleton lands.
- **VS Code: the server executable.** One VSIX per platform (`win32-x64`, `win32-arm64`, `linux-x64`, `linux-arm64`, `darwin-x64`, `darwin-arm64`, through `vsce package --target`), each embedding the executable from that platform's release zip, as the language server's packaging decision says ("bundled per platform"). A `godotXaml.server.path` setting overrides the embedded executable. The VSIXs are built after the release's zips are attached, and the macOS executable keeps its executable bit and its notarisation inside the VSIX.
- **VS Code: publishing.** The VS Code Marketplace and Open VSX, from a tag-gated release job whose publisher tokens live in a protected environment, as the `macos-signing` secrets do.
- **VS Code: language registration.** `.xscn` and `.xres` are their own languages, `xscn` and `xres`, coloured by VS Code's built-in XML TextMate grammar, with XML's comment and bracket configuration. Only this extension's language server attaches to them.
- **VS Code: toolchain.** TypeScript with `vscode-languageclient`, under the user-level Node/TypeScript conventions (pnpm, oxlint, oxfmt).

## Open

- The extensions' version against the release's (lockstep with `VersionPrefix`, or independent with a minimum server version).
- Visual Studio: the extension SDK (VisualStudio.Extensibility's `LanguageServerProvider`, or the legacy `ILanguageClient`), which also decides how GXAML-17's tool window and click-to-select are hosted.
- Visual Studio: the content type (its own, based on XML, or the built-in XML editor), the IDE versions it targets, and how its two Windows executables are delivered.
- Whether either extension's work goes on a feature branch.
