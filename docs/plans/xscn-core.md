# The `.xscn`/`.xres` core (GXAML-5)

How GXAML-5 is built: four briefs in order, and the choices the owner's format rulings (`../DECISIONS.md`) leave to the implementation, each settled here by the orchestrator and open to the owner. Once the last brief lands, the rulings worth keeping move to `DECISIONS.md` and this file is archived.

## Briefs

1. **Metadata loader and enum member names.** `src/GodotXaml.Core/Metadata/` (`GodotMetadata.Load`, class chain walk, property lookup by Godot and C# name, theme overrides, typed defaults read with `GodotValueReader`) and `Names/GodotEnumNames.cs`, a port of Godot's enum prefix stripping (`bindings_generator.cpp`, `_determine_enum_prefix` and `_apply_prefix_to_enum_constants`, the forced prefix of the global `Error` enum, then `snake_to_pascal_case(name, true)`). Proven against GodotSharp's own enum names, read from the GodotSharp 4.7.2 NuGet package with `System.Reflection.Metadata` in a test (no `F:\` path in a public repo). If that check runs long, it moves to GXAML-23.
2. **Model, attribute names and typed attribute values.** `src/GodotXaml.Core/Xscn/`: the immutable model (document, namespaces, uids, resources, nodes, overrides, properties, connections, the `x:` extras, comments, spans), property name mapping, and the compact value codec with enum, flags and hint names.
3. **Parser over GuiLabs.Language.Xml.** A tolerant parse to the model that always returns a document plus diagnostics. Starts with a short check of the library's API in the restored package. Needs each class's signals in the metadata (GXAML-32) to tell a signal attribute from a script property. May split constructs (`x:Resources`, inline resources, `.xres` roots, `x:Connect`) into a part b.
4. **Canonical writer and default omission.** Layout and order per `DECISIONS.md`; a value equal to the metadata default, or `layout_mode` under a Container, is omitted, never where the element's or the parent's class is unknown. Hand-written golden fixtures (`tests/fixtures/xscn/canonical/` round-trip byte for byte; `tests/fixtures/xscn/format/` pairs prove the canonical rewrite).

## Implementation choices (orchestrator decisions)

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
- **Groups.** A group name holding a comma or edge whitespace makes `x:Groups` fall back to Godot array text.
