# Decisions

The owner's decisions about godot-xaml and the engine facts behind them. Read this before reversing one; a reversal edits the entry in place.

## The format is the only committed file

A scene is committed as `.xscn` and a resource as `.xres`; no `.tscn` or `.tres` sits beside it in the repo. The Godot addon registers a `ResourceFormatLoader` and a `ResourceFormatSaver` for both, so the Godot editor opens, edits and saves them directly, and visual editing in Godot keeps working.

Why it is possible, from the Godot 4.7.2 source (`F:\Godot\src-4.7.2`):

- A script can register both: `ResourceLoader.add_resource_format_loader` and `ResourceSaver.add_resource_format_saver` are bound in `core/core_bind.cpp`, and the virtuals cover uids (`_get_resource_uid`, `_set_uid`), dependencies (`_get_dependencies`) and the FileSystem dock's move and rename (`_rename_dependencies`) in `core/io/resource_loader.h` and `core/io/resource_saver.h`.
- The editor's scene Open and Save dialogs list the extensions every registered loader and saver reports for `PackedScene` (`ResourceLoader::get_recognized_extensions_for_type` and `ResourceSaver::get_recognized_extensions` in `editor/editor_node.cpp`).
- "Run current scene" passes the scene as `--scene <path>` (`editor/run/editor_run.cpp` L151), which takes any extension. Only a bare positional path on the command line must end in `.scn`, `.tscn`, `.escn`, `.res` or `.tres` (`main/main.cpp`).
- Export converts only `.tscn` and `.tres` to binary (`editor/export/editor_export_platform.cpp` L1714), so the addon converts `.xscn` and `.xres` itself at export ("Exported games load binary through a remap").
- About ten other places in the editor match `.tscn` by name, none of them on the open, edit, save or run path (for example the large-file warning in `editor/editor_node.cpp` L2539 and the FileSystem dock's open action in `editor/docks/filesystem_dock.cpp`).

## The addon registers from C#, as global classes

The loader and saver are C# classes marked `[GlobalClass]` and `[Tool]`, with bases `ResourceFormatLoader` and `ResourceFormatSaver`. No `EditorPlugin` adds them and no GDScript shim is needed.

An `EditorPlugin` that adds them by hand does not last: on every script-class update the editor removes every script-backed loader and saver and re-adds only global classes (`editor/file_system/editor_file_system.cpp:2199-2205`, `core/io/resource_loader.cpp:1684-1713`, `core/io/resource_saver.cpp:254-283`). And an assembly reload recreates script instances only for tool scripts (`modules/mono/csharp_script.cpp:920-952`).

Measured on Godot 4.7.2 .NET with the GXAML-3 spike; `godot/editor-checks.ps1` runs the same checks on the real addon:
- **Headless.** `--import` and a game run both find the C# global classes. A `.xscn` loads, instances inside a `.tscn`, saves and reloads, and runs with `--scene` the way F6 does.
- **In the editor.** These all passed:
  - The Open and Save dialogs' extension lists hold `xscn`.
  - A `.xscn` opens as a scene tab, is edited and saved back as `.xscn`, and runs as the current scene.
  - It is instanced into a `.tscn`, which records its path and uid.
  - A move resolves through the uid sidecar.
  - It still saves as `.xscn` after a C# rebuild with the scene open, and reopens after an editor restart.
- **Rebuild.** Running a scene builds C# first (`editor/run/editor_run_bar.cpp:350-353` into GodotTools' `_Build`). The editor then reloads the assembly when its file is newer (`GodotTools/HotReloadAssemblyWatcher.cs`, `modules/mono/csharp_script.cpp:594-623`). The spike saw the build rewrite the assembly and the scene save correctly afterwards, but no script can observe the reload itself, so the addon's tests prove it with a counter that the reloaded assembly resets.
- **First import.** A brand-new project's first import writes no `.uid` sidecar for a `.xscn`: the first file scan runs before the C# global classes register, so no loader claims the file yet, and the file does not show in the FileSystem dock at all, even with a sidecar. The second open shows it and writes the sidecar. The addon must give every `.xscn` a uid before a scene references it.
- **By hand.** The owner checked the rest in the editor, and both passed:
  - Renaming a `.xscn` in the FileSystem dock renames its `.uid` sidecar too, rewrites the `.tscn` that instances it (path and uid), and the instance still loads.
  - The Open Scene dialog lists `*.xscn` and shows the file.

## A project installs the addon by copying it, with one package reference

A project copies `addons/godot_xaml/` and adds one `PackageReference` to `GodotXaml.Core`. An update raises the package version and recopies the folder. The folder is copied because Godot registers a global class only from the game project's own sources: a `.cs` file inside a NuGet package gets a `res://` path outside the project. A later `godot-xaml install` CLI verb can automate both steps. The folder's `.editorconfig` turns off IDE0130 and CS8632, so a host project that treats warnings as errors builds it; the steps are in the README.

## XAML syntax with C# names

The format is XML in the XAML style: an element per node, its type as the tag, its properties as attributes. Names are those of Godot's C# API: PascalCase properties (`CustomMinimumSize`, `SizeFlagsHorizontal`) and C# enum member names as values (`MouseFilter="Ignore"`, `StretchMode="KeepAspectCentered"`, `SizeFlagsHorizontal="ExpandFill"`), never the backing integers a `.tscn` stores.

- `Name="%Press"` marks a unique name (`unique_name_in_owner`), the way `GetNode("%Press")` reads it.
- A value equal to the default is not written, and neither is a value the parent decides: `layout_mode` under a `Container` is always Container, so it is omitted. Outside a Container `layout_mode` is always written: its default comes from an unparented instance, and Godot's loader honours a value that is written (`scene/gui/control.cpp:981-982`).
- Enum names come from Godot itself, dumped to a metadata file by a headless Godot run, so tools outside Godot (the language server, Visual Studio) can name them too. A property's hint string is not enough: it holds display names (`mouse_filter`'s hint is `Stop,Pass (Propagate Up),Ignore`) and leaves out combined constants such as `ExpandFill`, so the dump takes the enum's members from `ClassDB.class_get_enum_constants`, linked to the property by its enum type.

## Attribute values are typed by the metadata

A property whose type the metadata knows is written in compact XAML text: vectors as their components (`CustomMinimumSize="640, 0"`), `Color` as four floats (`Modulate="1, 1, 1, 0.5"`, never hex, which cannot hold Godot's float32 channels), strings and `StringName`s unquoted (`ThemeTypeVariation="TitleLabel"`), `NodePath`s as the bare path, and files as `res://` paths.

Every other value is written as Godot's own value text, exactly as a `.tscn` holds it: transforms, bases, quaternions, arrays, dictionaries, packed arrays, and every property of a script (`[Export]`), whose type the core cannot know (`Target='NodePath("../..")'`).

The property's type decides how its text is read, never the text's first character. A property the metadata types as `String` or `StringName` is always literal text, so `Text="[b]Bold[/b]"` and `Text="{0} items"` need no escape. A property with no known type (a script property, or one Godot declares as any Variant) is always Godot value text, its strings quoted (`Title='"[Esc] to close"'`).

On an element whose class the core cannot know without opening another file (an instanced scene, `<x:Override>`), a property is typed by its C# name across every Godot class: when every class that has the name agrees on its type and enum, the value is compact text (`AnchorsPreset="FullRect"`, `CustomMinimumSize="640, 0"`); when they disagree (`Position`, `Size`, `Mode`), it is Godot value text (`Position="Vector2(10, 20)"`).

This typing depends on the metadata of one Godot minor version, so a new version can change how a file is read: a name that becomes ambiguous, or a property whose type or enum changes.

Each Godot release that changes typing gets an upgrader that reads a project's files with the old version's metadata and writes them with the new one. When a new metadata file is added, a test lists every name whose typing differs from the previous version's, so whether a release needs an upgrade step is always known.

Booleans are written `True` and `False` (either case is read), and a float has no trailing `.0` (`AnchorRight="1"`), as vector components already have none.

## Format elements and attributes take the `x:` prefix

The default XML namespace is Godot's classes. Everything the format adds rather than Godot's property list takes the reserved `x:` prefix: the uid table (`<x:Uids>`), overrides (`<x:Override>`), and the attributes `x:Index`, `x:UniqueId`, `x:Editable` and `x:Groups`. A script property named `Index` or `Groups` therefore never clashes with them. `Name` and `Script` stay unprefixed, since they are Godot's own `name` and `script`.

## Overrides inside an instance are named by path

A node inside an instanced scene that the scene changes is written as `<x:Override Path="Centre/Panel">`, a child of the instance element, holding only the changed properties; nodes the scene adds under it nest inside it. The path is relative to the instance, so the core parses the file without opening the instanced scene to learn the type of each node on the way. `x:Editable="True"` on the instance element is the `.tscn`'s `[editable path=…]`.

## Slash-named properties are dotted attributes

A Godot property whose name holds a `/` (`theme_override_constants/separation`, `metadata/_tab_index`, `shader_parameter/albedo`) is written as a dotted attribute: the part before the slash in PascalCase, the key after it verbatim, since keys are free strings that would not survive a case change (`ThemeOverrideConstants.separation="12"`, `Metadata._tab_index="0"`).

A value that needs an element, such as an inline resource, uses the same name as a property element (`<VBoxContainer.ThemeOverrideStyles.panel>`). A key that is not a legal XML name falls back to `<x:Prop Name="…" Value="…" />` with the Godot name verbatim.

The value follows the typed-values rule. Each theme-override family has one fixed type (`scene/gui/control.cpp:400-417`: colors are `Color`, constants and font sizes `int`, fonts `Font`, icons `Texture2D`, styles `StyleBox`), so the metadata file records each family's type and the core writes them in compact text (`ThemeOverrideColors.font_color="1, 0.8, 0, 1"`). `metadata/…` and `shader_parameter/…` values have no fixed type, so they are written as Godot value text.

## Signals are attributes on the emitter

A connection is written on the node that emits the signal, like XAML's `Click="Handler"`. The common case, received by the scene root with default flags, is an attribute named by the signal's C# name whose value is the method (`<Button Name="Update" Pressed="on_update_button_pressed" />`).

Any other receiver, non-default flags, binds or unbinds use an element on the emitter, `<x:Connect Signal="Pressed" To="Panel" Method="Hide" Flags="Deferred, OneShot" Binds='[1, "a"]' />`, as does a signal whose name clashes with a property. The method name is the script's own and is never renamed.

`Signal` holds the C# name everywhere, mapped by the emitter's class, or on an instance, an override or an unknown class by the one Godot name every engine class with that C# name agrees on. A C# script's signal keeps its PascalCase name in Godot (`Finished`), which that mapping would read back as an engine signal (`finished`), so where `Signal` would read back as a different signal the writer uses `GodotSignal="Finished"`, holding Godot's own name verbatim. An `<x:Connect>` with both `Signal` and `GodotSignal` is `GX0118`; one with neither is `GX0108`.

## Shared sub-resources live in an `x:Resources` table

A sub-resource used once is written inline, as a property element. One used by several properties, or referenced from inside Godot value text (an array or dictionary), sits in an `<x:Resources>` table at the top of the root with an `x:Key`, like XAML's resource dictionaries. An attribute refers to it as `{Key}`; Godot value text refers to it as `SubResource("Key")`. Inlining a shared resource twice would make Godot load two separate objects. A resource used once on a property that has no attribute name, and so no property element (a Theme's `Button/styles/normal`, a key that is not an XML name), sits in the table too, referenced from `<x:Prop>`.

## Canonical attribute order: Name, Script, then alphabetical

The canonical writer puts `Name` first and `Script` second, then every other property in ordinal alphabetical order, then the `x:` attributes. The order needs no metadata, so a new Godot minor version never reorders a file.

## One error-tolerant XML parser

The core parses `.xscn` and `.xres` with GuiLabs.Language.Xml (formerly Microsoft.Language.Xml; Apache-2.0, targets net8.0 and netstandard2.0), a full-fidelity parser that keeps every span and builds a tree from half-typed text. Every host uses it: the Godot loader treats any error node as a load error, and the language server reports errors as diagnostics and keeps going. One parser means one set of parsing rules everywhere.

A comment is legal XML only between elements, never inside a start tag (XML 1.0, production `STag`), so a comment attaches to the element it precedes.

## Canonical layout

- A start tag stays on one line when it fits in 100 columns; otherwise every attribute goes on its own line, indented 4 spaces from the tag.
- Elements indent 2 spaces. No XML declaration; UTF-8 without a BOM, LF line ends, a final newline.
- Inside an element: `<x:Uids>` first, then `<x:Resources>`, then property elements, then child nodes in scene order.

## Integer properties with a hint but no enum

Nine integer properties carry an enum-style hint with no C# enum behind it (`layout_mode`, `anchors_preset`, `ProgressBar.fill_mode` and others). They are written by the hint's display name for the stored index, in PascalCase (`AnchorsPreset="FullRect"`, `LayoutMode="Anchors"`); the reader also accepts the integer.

## Where the rest of a `.tscn` goes

- `unique_id` is kept as `x:UniqueId`, the last attribute, wherever the `.tscn` has it, so a save from the editor does not churn ids and 4.7's id paths keep resolving.
- An inherited scene is a root element whose tag names a scene (`<c:Modal …>` at the root); no marker, since a root cannot be an instance.
- `Script="res://…"` is an attribute, as the inspector shows it.
- Groups are `x:Groups="a, b"`, sorted as Godot sorts them.
- A node's `index`, a non-root `owner`, `instance_placeholder`, `node_paths` and 4.7's `parent_id_path`, `owner_uid_path`, `from_uid_path` and `to_uid_path` are kept as `x:` attributes (`x:Index`, `x:Owner`, `x:InstancePlaceholder`, `x:NodePaths`, …), written only where the `.tscn` has them, so a conversion without a running Godot round-trips. An instance placeholder, which a `.tscn` writes with no `type`, is an `<InstancePlaceholder>` element holding `x:InstancePlaceholder="res://…"`, the class Godot builds it as; its properties are never dropped as defaults, since Godot saves them against the instanced scene.
- Not stored: `load_steps` (4.7.2 no longer writes it), `format` (chosen again on conversion back), `script_class` (it follows from the script), and `ext_resource` and `sub_resource` ids (references are paths and keys; ids are made again on conversion back). The scene's own uid lives in its `.uid` sidecar.

## The metadata file

- The dump tool is a GDScript run by headless Godot (`godot --headless --script`), with no build step, so it shares no files with the C# core.
- Its output is JSON, one file per Godot minor version (`metadata/godot-4.7.json`). Each class lists only its own properties and signals, with a link to its parent; names containing `/` are not properties. Each class also records the inherited defaults it changes (`defaultOverrides`), since Godot saves a property only when it differs from the default for the node's own class (`Button` has `focus_mode` All).

  The exception is `themeOverrides`, which a class that is `Control` or `Window` or inherits one carries as its full list, inherited items included: each `theme_override_<family>/<item>` entry with its type, hint and hint string, read from an instance, so the file records each family's type and the item names. Defaults are stored as Godot's own value text (`Vector2(0, 0)`), the grammar a `.tscn` uses, so the core reads them with its `.tscn` value reader.

## Test fixtures are synthetic

The `.tscn`/`.tres` reader and writer are tested on small scenes and resources made for the tests, one per section and value type, then re-saved by headless Godot 4.7.2 so the expected bytes are 4.7.2's own. They are committed. A node's `unique_id` cannot be pinned from a script, so regenerating the scene fixtures rewrites those numbers and nothing else. The owner's projects are not copied into this public repo: the converter runs on them in place (GXAML-8).

## Canonical formatting on save

Every save, by the Godot editor or the formatter, writes one fixed layout, like gofmt: diffs come only from real changes. Comments are kept, attached to the node or attribute they precede; hand line breaks and attribute order are not.

A save goes through `.tscn` text, which holds no comments, so they are carried over from the file being replaced. A node keeps its comments across a rename or a move: it is matched by its `x:UniqueId` first, then by its path from the root. A comment the save cannot carry is dropped with a warning naming it, never silently.

A save keeps an `x:Resources` key: the saver pairs a sub_resource with the previous document's entry by id first (the id the conversion back gives the entry, the same conversion the loader ran, and the type must agree), so an edited resource keeps its key and every reference, and its comments carry. A resource with no id hit falls back to the same `resource_name` or equal properties.

When an id pairs, the original keeps the key and a value-equal duplicate takes `Type1`; a resource whose type changed under the same id gets a new key; a keyed entry used once goes inline and drops its key, which no duplicate then takes.

## References: paths inline, XML namespaces for scenes

- A file is referenced by its `res://` path where it is used (`Texture="res://Art/frame.png"`).
- Each referenced file's uid is kept in an `<x:Uids>` table the tools maintain, so a move made outside the editor can still be resolved.
- A scene folder maps to an XML namespace (`xmlns:c="res://Scenes/Components/"`), so an instanced scene is a tag: `<c:Modal Name="%Confirm" />`.
- An inline sub-resource is a XAML property element (`<PanelContainer.ThemeOverrides><StyleBoxFlat … /></PanelContainer.ThemeOverrides>`).

## C# throughout

One C# core (parser, writer, metadata, `.tscn` conversion) serves the Godot addon, the CLI, the language server and the Visual Studio extension. The Godot addon is C#; the target is Godot 4.7+ .NET.

## The core's implementation choices

Choices the format rulings above leave to the core, made by the orchestrator while the core was built; they are open to the owner like any orchestrator decision.

- **Tags** are the C# class names (`CpuParticles2D`), following "XAML syntax with C# names".
- **Script properties** keep their stored name verbatim (`rowScene`); one that collides with an engine property's C# name uses `x:Prop`.
- **The `x:` namespace** is `https://github.com/leftos/godot-xaml/format/1`, which doubles as the format version. There is no default `xmlns`: unprefixed tags are Godot classes.
- **Scene namespaces.** The project root folder is `xmlns:res="res://"`. A tag's file extension comes from the matching `<x:Uids>` path, else `.xscn`. A file name that is not an XML name uses `<x:Instance Scene="res://…">`. Namespace declarations sit only on the root.
- **Diagnostics** live in `GodotXaml.Core.Diagnostics`: stable `GX` codes, UTF-16 offsets, a line map giving 0-based positions for the language server and 1-based ones for the loader. Any error fails a Godot load.
- **Unknown elements.** An unknown class tag (a GDExtension class) is kept as a node of unknown type with a warning. An unknown `x:` element or attribute is an error and is not kept.
- **String or StringName.** The value's kind follows the metadata default when the declared type and the default disagree (`theme_type_variation` is declared String with a StringName default), so conversion back keeps `&"…"`.
- **Flags** are written as .NET's `[Flags]` text joined with `", "`; leftover bits mean the integer. FLAGS-hint integers without an enum take bit `1 << i` unless the hint gives `:value`. A hint display name becomes PascalCase by splitting on anything not a letter or digit. An empty hint means the integer.
- **References.** An explicit null is `{x:Null}`. A file inside Godot value text is `Resource("res://…")`; `ExtResource(…)` is rejected in `.xscn`.
- **Rect2, Plane, AABB** are Godot value text.
- **Quoting.** Values use `"`, or `'` when they hold `"` and no `'`. `&`, `<`, the quote character, newline, carriage return and tab are written as references; reading decodes per XML.
- **Order details.** `xmlns` declarations first on the root; `x:Key` first on a resource; signal attributes join the alphabetical run; `x:` attributes alphabetical with `x:UniqueId` last; property elements and `x:Prop` by name; `x:Connect` in model order (connection order is call order); `<x:Uids>` by path; `<x:Resources>` in model order; no blank lines.
- **Connection flags.** Persist is implied and never written in `Flags`; a connection without Persist writes the integer.
- **Property elements** name their own tag as owner (`<c:Modal.Theme>`, `<x:Override.Theme>`).
- **A value that does not fit its type's compact form** (an int in a float property, a script property on an instance whose stored name matches an engine property of another type) is written as `<x:Prop Name="…" Value="…" />` with Godot value text. The codec decides by reading its own attribute text back and comparing, so every round trip is exact. On an untyped attribute or `x:Prop`, `Resource("res://…")` alone reads as a file and `{Key}` as a resource key.
- **Groups.** A group name holding a comma or edge whitespace makes `x:Groups` fall back to Godot array text.
- **Uids** are `<x:Uid Path="res://…" Value="uid://…" />` entries.
- **Connections.** `<x:Connect>` names its signal with `Signal`, or with `GodotSignal` (Godot's name verbatim) where `Signal` would read back as another signal; it carries `FromUidPath` and `ToUidPath` as comma-separated integers; `To` defaults to `.`; Persist is implied and flag names come from `Object.ConnectFlags`.
- **Attribute values** are entity-decoded by the parser, not the XML library. A bad reference is the library's `GX0001` and the value is kept as written.
- **Library errors** become `GX0001`. An element's diagnostics span its tag name, a value's diagnostics span the value. `GX0107` (unknown class) is the only warning.
- **The `x:` prefix** is whatever the root binds to the format URI. An element with an undeclared prefix is kept as an unknown node; such an attribute is dropped.
- **Signal attributes.** An attribute on a known class with no engine property of that C# name is recognised as a signal.
- **`x:Key`** is accepted only on an `x:Resources` entry; a repeated one is `GX0116`.
- **`x:Groups` and `x:NodePaths`** split on commas, or read as Godot array text when the value starts with `[`.
- **Comments** attach to the next node, property, connection, uid or resource read. Trailing ones attach to the element, property element or table they end. When the element they precede is not read, they carry over to the next element read.
- **A document with no root** gets a stand-in root and `GX0004`.
- **Writer entry.** `XscnWriter.Write(document, metadata)` returns the text: LF, a final newline, no XML declaration, 2-space indent. A start tag stays on one line up to 100 columns, else one attribute per line indented 4.
- **Writer attribute order.** `Name`, `Script`, the rest ordinal, then the `x:` attributes with `x:UniqueId` last. Content order is `x:Uids`, `x:Resources`, property elements and `x:Prop` by name, `x:Connect`, children.
- **Default omission** applies only on an element of a known class, against `GodotMetadata.FindDefault`; `layout_mode` is never compared with its default, and is omitted only under a Container. A property with comments or a repeated name is never omitted.
- **Signal attributes on write.** A connection is a signal attribute only in the simple case (scene root receiver, Persist only, no binds) and in a leading run that keeps call order; any other connection is `x:Connect`.
- **Writer `x:Prop`.** A property whose attribute name would read back as something else, is taken, has comments or repeats a name is written as `x:Prop`; once a name is written as an element, all properties of that name are.
- **Prefix clash.** A scene namespace whose prefix is `x` is written as `x1`, `x2`, and so on.
- **Characters XML 1.0 cannot hold.** A string with one is written as `x:Prop` with `\uXXXX` escapes, binds included; group and node-path names fall back to escaped array text. A plain-text field (a name, a path) with one throws `ArgumentException`, and a lone surrogate anywhere throws.
- **Writer comments.** A carriage return in a comment becomes LF. An empty table that has comments is still written.
- **Malformed structure.** A duplicate attribute is `GX0113` (the first is kept), a scene-prefixed attribute `GX0114`, a second table `GX0115`, and a class tag that is not a Resource where a resource goes (a property element, an `x:Resources` entry, an `.xres` root) `GX0117`, the element still read. `Name=""` and `Name="%"` are `GX0104`.

## Sub-resource ids follow where they are used

A built-in sub-resource's id is minted from where it is used: its site key (the element path and property it sits in, nested), or its `x:Resources` key. It does not depend on its position among the sub-resources, so an edit elsewhere in the file keeps every other id.

Why: the format stores no ids (the owner's "Not stored" ruling, "Where the rest of a `.tscn` goes"), so a save and the next load must compute the same id from the document alone. The saver gives each sub-resource the path `<file>::<id>` the next load mints, and a load registers its sub-resources in the cache under the same paths, so the open resource and a reload are one object.

Godot fact (4.7.2): the C# dispatch of a `ResourceFormatLoader._load` override copies the returned Variant and never disposes the original (`ScriptMethodsGenerator.cs` in GodotSharp's source generators, and `VariantUtils.generic.cs`). A C# `_Load` that does not dispose its Variant keeps the returned scene alive, and its sub-resources stay in the cache.

## Exported games load binary through a remap

An `EditorExportPlugin`, registered by an `EditorPlugin` the project enables, loads each `.xscn` and `.xres` through the loader at export, saves Godot binary and adds it with `add_file(path, bytes, remap: true)`, so the exported game loads binary through Godot's `.remap` and runs no addon code at load. The raw XML is not shipped. The loaders are packed too, as a fallback and for a `.xscn` autoload's type check (`main/main.cpp`, which asks `get_resource_type` of the original path, and that lookup does not follow a remap). The binary sits under `res://.godot/exported/godot-xaml/`, named as Godot names its own conversions, so a project holding both `a.xscn` and `a.scn` gets no clash.

The `.xres` loader reports the type of the file's root tag (a prefix read, not a full parse), so a `Theme.xres` is a Theme in the editor's pickers and in "export selected resources"; an unreadable first tag reports `Resource`.

A file that fails to convert fails the export, naming the file and the error. Godot 4.7.2 gives an export plugin no way to change the export's result: `export_project_files` never reads plugin messages, and a headless `--export-pack` exits 0 whatever they say. So a failure is an `AddMessage(Error)` and a `Skip()`, the way Godot's own .NET export plugin reports one: the editor's export dialog shows "Failed.", and headless the `ERROR: godot-xaml:` line is the signal.

The filtered global class list keeps a script only when the script is exported, and a loader dropped from it silently fails every `.xscn` that was not converted. So the export plugin checks, in every export mode, that both loader scripts survive the mode's own rule and the include and exclude filters, and otherwise fails the export naming the filter to fix.

## Version 1 scope

- `.xscn` scenes and `.xres` resources, opened, edited and saved in Godot 4.7+.
- A lossless `.tscn`/`.tres` converter, run on the owner's projects (delve-the-dungeon first), with a check that converting back yields the same scene.
- Visual Studio: completions, enum names and errors while typing, and a live preview pane rendered by Godot; clicking a node in the preview selects its element in the text.
- VS Code: the same language server.
- Agent tooling: a CLI (convert, check, format) and godot-mcp support for the new files.

Later versions: editing the selected node in Visual Studio's Properties window, then a full visual designer in Visual Studio.

## Releases

Version 1 ships in three releases, tracked as Linear's `vNext`, `vNext+1` and `vNext+2`:

1. **First release**, the one the owner's repos start trying: the core (model, parser, canonical writer), the `.tscn` conversion run on the owner's projects, the addon's loader, saver and export support, an install a project can follow, the CLI, and godot-mcp support for `.xscn`. A project can convert its scenes, edit them in Godot, ship a build, and let its agents preview and edit them.
2. **Second**: the language server and the VS Code extension, with the name checks against GodotSharp.
3. **Third**: the Visual Studio extension and its preview pane, with the addon's preview server.

The later versions above sit in no release. Everything godot-xaml needs from godot-mcp is owner-approved at High priority there.

## Friction from projects using godot-xaml is filed as GitHub issues

From the first release, every repo that uses godot-xaml files each friction (a conversion that comes out wrong, a load error, an editor save that changes a scene it should not, a tooling gap) as a GitHub issue on `leftos/godot-xaml`, one per friction, naming the version, what happened against what was expected, and the workaround, as godot-mcp's friction rule does. The repo is public, so a private project's scene goes in as a minimal repro, never pasted whole.

## A project conversion is all or nothing, and backs up what it removes

`godot-xaml convert --project <root>` converts both ways: forward (`.tscn`/`.tres` to `.xscn`/`.xres`) by default, and back with `--back`, each rewriting references, scripts and `project.godot` the same way.

It skips `addons/` (third-party addon scenes stay as they are, and references to them are left alone), `.godot/`, every folder whose name starts with `.`, and every folder holding a `.gdignore` file. There is no flag to include `addons/`.

It always backs up what it removes or edits, unless the user passes `--nobackup`. The backup is a folder under the root, not the Recycle Bin, since the bin cannot meet "always": Windows silently deletes a file it cannot recycle and other systems have no bin.

It is all or nothing. The outputs are written to a hidden staging folder first. If any source fails, the staging folder is deleted and the project is left exactly as it was, with every error listed in that run. Only when every source converts are the originals backed up, the outputs moved in, the scripts and `project.godot` edited, and the originals removed. A failure after the first move is reported as a part-converted project, naming the backup to restore from; there is no automatic rollback.

## A project conversion leaves the files Godot loads before any addon as text

`convert --project` leaves as text the files named by `gui/theme/custom`, `gui/theme/custom_font`, `audio/buses/default_bus_layout` (its default path `res://default_bus_layout.tres` unless an untagged key sets it, when that file exists), `display/mouse_cursor/custom_image`, `rendering/environment/defaults/default_environment` and `internationalization/locale/translations`, plus every `.tres`/`.tscn` those reach through ext_resources. Everything else converts.

A `uid://` value is resolved from the sources' headers, and a feature-tagged key counts. A `uid://` that names no file leaves nothing and prints nothing. The CLI does not read `override.cfg`. Each left file is named on an uncounted line, and the run neither converts, renames, backs up, removes nor rewrites references to it.

Why, from the Godot 4.7.2 source: Godot loads these files before `ResourceLoader::add_custom_loaders()` (`main/main.cpp:3880-3881` and `:3691`, before `:4494`), so the addon's loaders do not exist yet; and a left theme whose ext_resource was converted fails whole at startup (`scene/resources/resource_format_text.cpp:147-160`). Whether a left file hits an export is unmeasured.

## Name

The repo is `godot-xaml`, public at `github.com/leftos/godot-xaml`, MIT. Scenes are `.xscn`, resources `.xres`.
