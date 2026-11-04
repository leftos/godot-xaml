# Decisions

The owner's decisions about godot-xaml and the engine facts behind them. Read this before reversing one; a reversal edits the entry in place.

## The format is the only committed file

A scene is committed as `.xscn` and a resource as `.xres`; no `.tscn` or `.tres` sits beside it in the repo. The Godot addon registers a `ResourceFormatLoader` and a `ResourceFormatSaver` for both, so the Godot editor opens, edits and saves them directly, and visual editing in Godot keeps working.

Why it is possible, from the Godot 4.7.2 source (`F:\Godot\src-4.7.2`):

- A script can register both: `ResourceLoader.add_resource_format_loader` and `ResourceSaver.add_resource_format_saver` are bound in `core/core_bind.cpp`, and the virtuals cover uids (`_get_resource_uid`, `_set_uid`), dependencies (`_get_dependencies`) and the FileSystem dock's move and rename (`_rename_dependencies`) in `core/io/resource_loader.h` and `core/io/resource_saver.h`.
- The editor's scene Open and Save dialogs list the extensions every registered loader and saver reports for `PackedScene` (`ResourceLoader::get_recognized_extensions_for_type` and `ResourceSaver::get_recognized_extensions` in `editor/editor_node.cpp`).
- "Run current scene" passes the scene as `--scene <path>` (`EditorRun::run` in `editor/run/editor_run.cpp`), which takes any extension. Only a bare positional path on the command line must end in `.scn`, `.tscn`, `.escn`, `.res` or `.tres` (`main/main.cpp`).
- Export converts only `.tscn` and `.tres` to binary (`EditorExportPlatform::export_project_files` in `editor/export/editor_export_platform.cpp`, its `force_binary` test), so the addon converts `.xscn` and `.xres` itself at export ("Exported games load binary through a remap").
- Other places in the editor match `.tscn` by name, none of them on the open, edit, save or run path (for example the large-file warning in `EditorNode::_save_scene` in `editor/editor_node.cpp` and the FileSystem dock's open action in `editor/docks/filesystem_dock.cpp`).

## The addon registers from C#, as global classes

The loader and saver are C# classes marked `[GlobalClass]` and `[Tool]`, with bases `ResourceFormatLoader` and `ResourceFormatSaver`. No `EditorPlugin` adds them and no GDScript shim is needed.

An `EditorPlugin` that adds them by hand does not last: on every script-class update the editor removes every script-backed loader and saver and re-adds only global classes (`EditorFileSystem::_update_script_classes` in `editor/file_system/editor_file_system.cpp`, `ResourceLoader::add_custom_loaders` and `remove_custom_loaders` in `core/io/resource_loader.cpp`, `ResourceSaver::add_custom_savers` and `remove_custom_savers` in `core/io/resource_saver.cpp`).

And an assembly reload recreates script instances only for tool scripts (`CSharpLanguage::reload_assemblies` in `modules/mono/csharp_script.cpp`).

Measured on Godot 4.7.2 .NET with the GXAML-3 spike; `godot/editor-checks.ps1` runs the same checks on the real addon:
- **Headless.** `--import` and a game run both find the C# global classes. A `.xscn` loads, instances inside a `.tscn`, saves and reloads, and runs with `--scene` the way F6 does.
- **In the editor.** These all passed:
  - The Open and Save dialogs' extension lists hold `xscn`.
  - A `.xscn` opens as a scene tab, is edited and saved back as `.xscn`, and runs as the current scene.
  - It is instanced into a `.tscn`, which records its path and uid.
  - A move resolves through the uid sidecar.
  - It still saves as `.xscn` after a C# rebuild with the scene open, and reopens after an editor restart.
- **Rebuild.** Running a scene builds C# first (`EditorRunBar::_run_scene` in `editor/run/editor_run_bar.cpp` calls `EditorNode::call_build`, into GodotTools' `_Build`). The editor then reloads the assembly when its file is newer (`GodotTools/HotReloadAssemblyWatcher.cs`, `CSharpLanguage::is_assembly_reloading_needed` in `modules/mono/csharp_script.cpp`).

  The spike saw the build rewrite the assembly and the scene save correctly afterwards, but no script can observe the reload itself, so the addon's tests prove it with a counter that the reloaded assembly resets.
- **First import.** A brand-new project's first import writes no `.uid` sidecar for a `.xscn`: the first file scan runs before the C# global classes register, so no loader claims the file yet, and the file does not show in the FileSystem dock at all, even with a sidecar. The second open shows it and writes the sidecar. The addon must give every `.xscn` a uid before a scene references it.
- **By hand.** The owner checked the rest in the editor, and both passed:
  - Renaming a `.xscn` in the FileSystem dock renames its `.uid` sidecar too, rewrites the `.tscn` that instances it (path and uid), and the instance still loads.
  - The Open Scene dialog lists `*.xscn` and shows the file.

## A project installs the addon by copying it, with one package reference

A project copies `addons/godot_xaml/` and adds one `PackageReference` to `GodotXaml.Core`. An update raises the package version and recopies the folder. The folder is copied because Godot registers a global class only from the game project's own sources: a `.cs` file inside a NuGet package gets a `res://` path outside the project.

A later `godot-xaml install` CLI verb can automate both steps. The folder's `.editorconfig` turns off IDE0130 and CS8632, so a host project that treats warnings as errors builds it; the steps are in the README.

## XAML syntax with C# names

The format is XML in the XAML style: an element per node, its type as the tag, its properties as attributes. Names are those of Godot's C# API: PascalCase properties (`CustomMinimumSize`, `SizeFlagsHorizontal`) and C# enum member names as values (`MouseFilter="Ignore"`, `StretchMode="KeepAspectCentered"`, `SizeFlagsHorizontal="ExpandFill"`), never the backing integers a `.tscn` stores.

- `Name="%Press"` marks a unique name (`unique_name_in_owner`), the way `GetNode("%Press")` reads it.
- A value equal to the default is not written, and neither is a value the parent decides: `layout_mode` under a `Container` is always Container, so it is omitted. Outside a Container `layout_mode` is always written: its default comes from an unparented instance, and Godot's loader honours a value that is written (`Control::_get_layout_mode` in `scene/gui/control.cpp`).
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

The default XML namespace is Godot's classes. Everything the format adds rather than Godot's property list takes the reserved `x:` prefix: the tables (`<x:Uids>`, `<x:Resources>`), the elements `<x:Instance>`, `<x:Override>`, `<x:Connect>` and `<x:Prop>`, and the attributes `x:Editable`, `x:Groups`, `x:Index`, `x:InstancePlaceholder`, `x:NodePaths`, `x:Owner`, `x:OwnerUidPath`, `x:ParentIdPath`, `x:UniqueId` and `x:Key`.

A script property named `Index` or `Groups` therefore never clashes with them. `Name` and `Script` stay unprefixed, since they are Godot's own `name` and `script`.

## Overrides inside an instance are named by path

A node inside an instanced scene that the scene changes is written as `<x:Override Path="Centre/Panel">`, a child of the instance element, holding only the changed properties; nodes the scene adds under it nest inside it. The path is relative to the instance, so the core parses the file without opening the instanced scene to learn the type of each node on the way.

The way back still reads the instanced scene, through the host's scene lookup, but only to put an instanced node's properties in Godot's order. `x:Editable="True"` on the instance element is the `.tscn`'s `[editable path=…]`.

## Slash-named properties are dotted attributes

A Godot property whose name holds a `/` (`theme_override_constants/separation`, `metadata/_tab_index`, `shader_parameter/albedo`) is written as a dotted attribute: the part before the slash in PascalCase, the key after it verbatim, since keys are free strings that would not survive a case change (`ThemeOverrideConstants.separation="12"`, `Metadata._tab_index="0"`).

A value that needs an element, such as an inline resource, uses the same name as a property element (`<VBoxContainer.ThemeOverrideStyles.panel>`). A key that is not a legal XML name falls back to `<x:Prop Name="…" Value="…" />` with the Godot name verbatim; so does an empty key (`libraries/`, an AnimationPlayer's default library), since `<AnimationPlayer.Libraries.>` names no property.

The value follows the typed-values rule. Each theme-override family has one fixed type (`Control::_get` in `scene/gui/control.cpp`: colors are `Color`, constants and font sizes `int`, fonts `Font`, icons `Texture2D`, styles `StyleBox`), so the metadata file records each family's type and the core writes them in compact text (`ThemeOverrideColors.font_color="1, 0.8, 0, 1"`). `metadata/…` and `shader_parameter/…` values have no fixed type, so they are written as Godot value text.

## Signals are attributes on the emitter

A connection is written on the node that emits the signal, like XAML's `Click="Handler"`. The common case, received by the scene root with default flags, is an attribute named by the signal's C# name whose value is the method (`<Button Name="Update" Pressed="on_update_button_pressed" />`).

Any other receiver, non-default flags, binds or unbinds use an element on the emitter, `<x:Connect Signal="Pressed" To="Panel" Method="Hide" Flags="Deferred, OneShot" Binds='[1, "a"]' />`, as does a signal whose name clashes with a property. The method name is the script's own and is never renamed.

`Signal` holds the C# name everywhere, mapped by the emitter's class, or on an instance, an override or an unknown class by the one Godot name every engine class with that C# name agrees on.

A C# script's signal keeps its PascalCase name in Godot (`Finished`), which that mapping would read back as an engine signal (`finished`), so where `Signal` would read back as a different signal the writer uses `GodotSignal="Finished"`, holding Godot's own name verbatim. An `<x:Connect>` with both `Signal` and `GodotSignal` is `GX0118`; one with neither is `GX0108`.

## Shared sub-resources live in an `x:Resources` table

A sub-resource used once is written inline, as a property element. One used by several properties, or referenced from inside Godot value text (an array or dictionary), sits in an `<x:Resources>` table at the top of the root with an `x:Key`, like XAML's resource dictionaries. An attribute refers to it as `{Key}`; Godot value text refers to it as `SubResource("Key")`.

Inlining a shared resource twice would make Godot load two separate objects. A resource used once on a property that has no attribute name, and so no property element (a Theme's `Button/styles/normal`, a key that is not an XML name), sits in the table too, referenced from `<x:Prop>`.

## Canonical attribute order: Name, Script, then alphabetical

The canonical writer puts `Name` first and `Script` second, then every other property in ordinal alphabetical order, then the `x:` attributes. The order needs no metadata, so a new Godot minor version never reorders a file.

The way back (`.xscn` to `.tscn`) restores Godot's property order instead, because a `.tscn` sets properties in file order and some depend on it (`control.cpp` anchors and grow direction, `base_button.cpp` `toggle_mode` before `button_pressed`).

An instance and an `InstancePlaceholder` take their class order from their scene's root; a node of an unknown type, or either of those with no scene lookup, keeps its source order. A node's ext_resources are numbered in this order too.

`PropertyOrder` sorts a section's properties stably: each class base first, with its own list; `theme_override_*` by the metadata's `ThemeOverrides` index, after Control's or Window's own list; then `script`; then the slash-named names the dynamic-family table does not place, so a script's own come after it; then script exports, then `metadata/*`.

A dynamic name, one a class adds in `_get_property_list` and so absent from the metadata, goes in its declaring class's place: after that class's own list and theme overrides, before the next class down the chain and before `script`. The classes and their name patterns come from a hand table, `DynamicPropertyFamilies`, read from the Godot source, since the metadata dump cannot list them. A name the table does not place keeps the shape rule: a slash name goes after `script`.

An instanced node, an inherited root and an `x:Override` take their class from the instanced scene, read through the host's scene lookup (`ConversionOptions.SceneFiles`), and then sort as any node of that class, so a script export stays after `script`. With no lookup, or a scene the lookup cannot read, the section keeps source order, and a scene it cannot read is warning GXC0118, naming the file and why.

A class is never guessed from a section's names: a guess put a script export named like a class property ahead of `script`, where Godot drops it (`SceneState::instantiate` ignores a failed `set`). A placeholder and an unknown type keep source order. Order among script exports and inside a dynamic family that Godot reads in any order (Theme) cannot be restored from the alphabetical `.xscn`, nor can the order of a family Godot lists in the order its entries were added (VisualShader `varyings/` and AnimationNodeBlendTree `nodes/`, which their `_get_property_list` walk in insertion order): `ResourceBackTests` compares such names as a set, from a reviewed list in the test. VisualShader `modes/` and `flags/` come back in the shader mode's render-mode order (`ShaderTypes`, `servers/rendering/shader_types.cpp`); `flags/` follows the section's last integer `mode` (each `VisualShader::set_mode` clears the flags), Spatial's when `mode` is absent or not an integer, and keeps source order for Fog, TextureBlit, an out-of-range or negative mode and a flag the mode does not list.

Every row of `DynamicPropertyFamilies` states one of seven kinds, and a test walks the metadata to check each claim:

`AnyOrder` (each name sets its value on its own, and the family keeps no index or key order), `SizedFirst` (a named count property, ranked before the family, sizes the entries; a row may carry the declaring class's key order and a nested level), `OnKey` (Godot lists entries by number in a fixed key order, creating an entry on its creating key or on any key), `ByMode` (the key order is one of several, chosen by a sibling property's integer value, so a name's rank may read a sibling property and not its pattern alone:

VisualShader `flags/` by `mode`), `Ranked` (a ranker method computes the family's whole rank; the row's listed class is optional: a ranker that reads a class list names it, and a metadata file without that class raises `InvalidDataException`, while one that reads none leaves it null), `ByName` (Godot lists the names in code-point order, as its `AlphCompare` sorts them) and `BySource` (the order comes from the contents of a sibling property's resource). The `OnKey` families and the sized families whose keys reset one another are the exceptions to source order:

Animation tracks on `type`, Skeleton3D bones on `name`, blend points on `node`, AnimationNodeTransition inputs on `name` (`AnimationNodeTransition::_set`; its `input_count` is editor-only and never stored), VisualShader nodes on `node` (`VisualShader::_set`), SceneReplicationConfig properties on `path` (`SceneReplicationConfig::_set`, which creates an entry only when its index equals the count so far) TileSet patterns (`TileSet::_set` with `add_pattern`, which appends at the next index), TileMap `layer_N/` (`TileMap::_set` adds layers up to the index on any key; each layer's keys in registration order: name, enabled, modulate, y_sort_enabled, y_sort_origin, z_index, navigation_enabled, tile_data) and MeshLibrary `item/N/` (`MeshLibrary::_set` creates an item on any key; items by id, each item's keys in `_get_property_list` order: name, mesh, mesh_transform, mesh_cast_shadow, shapes, navigation_mesh, navigation_mesh_transform, navigation_layers, preview).

They come back entry by entry in numeric order, each entry's creating key first and its other keys in the declaring class's own `_get_property_list` order. A VisualShader goes graph by graph in Godot's type order (vertex, fragment, light, start, process, collide, start_custom, process_custom, sky, fog, texture_blit), each graph's nodes by id, then its connections, as `VisualShader::_get_property_list` lists them (its nodes are an `RBMap` by id).

A TileSetAtlasSource comes back tile by tile (`DynamicPropertyFamilies.AtlasTileRank`, a `Ranked` row; `scene/resources/2d/tile_set.cpp`): tiles by (x, y), x first, numeric. Each tile holds `size_in_atlas`, `next_alternative_id`, `animation_columns`, `animation_separation`, `animation_speed`, `animation_mode`, then `animation_frame_N/duration` by N, then each alternative by numeric id (its marker, then its TileData keys), then unknown keys.

A TileData's keys go as the class properties in the metadata's list order, then occlusion layers (the compatibility key `occlusion_layer_N/polygon` first in its layer, then `polygon_M/polygon` by M), physics layers (`linear_velocity`, `angular_velocity`, then per polygon `points`, `one_way`, `one_way_margin`), `terrains_peering_bit` sides in Godot's neighbour order, navigation layers and custom data, every number numeric.

The owner ruled tiles by coordinates and alternatives by id, since the text keeps no record of the order Godot created them in; `UnorderedNames`' "TileSetAtlasSource tiles" entry in `ResourceBackTests` hides only tile and alternative order, moving whole tile and alternative blocks. The writer's alphabetical order stays.

Three more `Ranked` rows read no class list. An AudioBusLayout comes back bus by bus (`BusRank`; `servers/audio/audio_server.cpp`): each bus's keys, then its effects `effect/M/` by number, each effect's keys in order. An AudioEffectEQ's `band_db/<freq>_hz` come back by frequency (`BandRank`), the order every preset adds its bands in (`eq_filter.cpp`). A FontFile's `cache/N/` come back entry by entry (`FontCacheRank`;

`scene/resources/font.cpp`): the entry's keys, then its sizes by number, each size's metrics in order. Godot lists several sizes of one entry in the creation order of the text server's size cache (a HashMap), which the text keeps no record of, so they come back by number and not in Godot's order.

AudioStreamRandomizer `stream_N/`, LabelSettings `stacked_outline_N/` and `stacked_shadow_N/`, Skin `bind/N/`, SkeletonProfile `groups/N/` and `bones/N/`, and MeshInstance3D `surface_material_override/N` (by surface index) are sized families carrying their class's key order. Every entry list past ten comes back in numeric order.

Four more families come back in Godot's order. `instance_shader_parameters/` (CanvasItem, GeometryInstance3D) and ShaderGlobalsOverride `params/` are `ByName` rows: Godot lists them sorted by `AlphCompare`, so they come back in code-point order. ShaderMaterial `shader_parameter/` and MeshInstance3D `blend_shapes/` are `BySource` rows, read from the sibling `shader` or `mesh` through `SourceOrders`, a per-conversion cache.

A shader gives its local uniforms in declaration order, regrouped by `group_uniforms` as `ShaderMaterial::_get_property_list` does: groups in first-appearance order, and the ungrouped bucket where its first uniform appeared. `instance uniform` and `global uniform` are left out, as are uniforms hinted `hint_screen_texture`, `hint_normal_roughness_texture`, `hint_depth_texture` or `hint_blit_source0` to `3`. A shader with a preprocessor directive (a `#` with only blanks or comments before it on its line) keeps source order. A mesh gives an ArrayMesh's `_blend_shape_names` index.

The source is inline, an `x:Resources` entry by key, or an external `.gdshader` or text ArrayMesh read through the scene lookup (`ISceneFiles`) when the conversion has `SceneFiles` and `SourceKey`. Each file is read once per conversion, keyed by referrer, reference and the uid from `x:Uids`, then the reference's own uid.

A mesh file is parsed by the key the host serves it under: a text `.tres` as a `.tres`, and an `.xres` key as a resource, so `convert --back` orders `blend_shapes/` from the `.xres` converted in the same run. Anything unreadable keeps source order: a missing file, no lookup, a `.res` or `.glb` mesh, a `.tres` shader or a parse failure.

A missing file or a mesh that does not parse is warning GXC0122, once per file and reason at the first section that meets it; no lookup, a shader only the preprocessor could read, and a file that is not an ArrayMesh or has no blend shape names stay silent.

`UnorderedNames` hides two more families whose order the text cannot restore, because Godot walks a HashMap: FontFile `language_support_override/` and `script_support_override/` (the order the overrides were set in) and BoneMap `bone_map/` (the order the profile's bones were added in).

A sized family whose keys reset one another comes back in the declaring class's own order, with one nested level: a name ranks by entry, key, sub-entry, sub-key (the `subindex` and `subkey` groups of the row's pattern, with a `SubKeyOrder`). SpringBoneSimulator3D's `settings/N/` keys and the IterateIK3D subclasses' (CCDIK3D, FABRIK3D, JacobianIK3D) and SplineIK3D's are such: `SpringBoneSimulator3D::set_root_bone` and `ChainIK3D::set_root_bone` (and `set_end_bone`) with no skeleton zero the joints, and the spring collision setters are gated on `enable_all_child_collisions`, so a key set early loses what a later one restores. Entry and sub-entry lists (joints, collisions) past 9 come back in numeric order. IterateIK3D comes back in three passes in Godot's order: every setting's `target_node`, then each setting's chain keys, then every setting's joint keys (`settings/N/joints/M/`). SplineIK3D comes back in two: path and tilt keys, then chain keys.

A sized family whose keys reset nothing also carries its class's order once a fixture holds it, because the owner's ruling keeps every round trip byte-identical and Godot saves in `_get_property_list` order, not the `.xscn`'s alphabetical one. TwoBoneIK3D, LimitAngularVelocityModifier3D `chains/`, BoneTwistDisperser3D (its `joints/M/` as the nested level) and the BoneConstraint3D subclasses carry it: AimModifier3D, CopyTransformModifier3D and ConvertTransformModifier3D come back in two passes, every setting's BoneConstraint3D keys, then every setting's own keys. BoneTwistDisperser3D's `_validate_dynamic_prop` stores `end_bone_direction` only once `extend_end_bone` is set, and `twist_from` only while `twist_from_rest` is false, so its fixture sets both pairs.

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
- A node's `index`, a non-root `owner`, `instance_placeholder`, `node_paths` and 4.7's `parent_id_path` and `owner_uid_path` are kept as `x:` attributes (`x:Index`, `x:Owner`, `x:InstancePlaceholder`, `x:NodePaths`, `x:ParentIdPath`, `x:OwnerUidPath`), and a connection's `from_uid_path` and `to_uid_path` as `FromUidPath` and `ToUidPath` on its `<x:Connect>`, each written only where the `.tscn` has it, so a conversion without a running Godot round-trips.

  An instance placeholder, which a `.tscn` writes with no `type`, is an `<InstancePlaceholder>` element holding `x:InstancePlaceholder="res://…"`, the class Godot builds it as; its properties are never dropped as defaults, since Godot saves them against the instanced scene.
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

Only a sub-resource the saved file owns pairs by id, because Godot's text saver re-mints the second of two repeated ids and a sub-resource from another file can carry the same id.

When an id pairs, the original keeps the key and a value-equal duplicate takes `Type1`; a resource whose type changed under the same id gets a new key; a keyed entry used once goes inline and drops its key, which no duplicate then takes.

The first save of a hand-written file writes it in this canonical form, with no attempt to keep the file's own spelling: an instance written `x:Instance` becomes its scene's namespace tag, the `x:Uids` table and the nodes' `x:UniqueId` are added, and a relative reference is written as its `res://` path, since loading resolves it before Godot sees the scene. Comments still carry.

From then on a save of an unchanged scene changes nothing, so the one-time diff is the cost of a single form; keeping each file's own spelling would leave two forms of one scene and files with no uids for moves to resolve.

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
- **A getter's type wins.**

  Godot registers some properties with a type their getter does not return (`oversampling` is registered as float with `ADD_PROPERTY` in `AudioEffectPitchShift::_bind_methods` while `AudioEffectPitchShift::get_oversampling` returns int; `theme_type_variation` is declared String with a StringName default), and it saves the getter's value (`SceneState::_parse_node` in scene/resources/packed_scene.cpp); ClassDB's default is the raw getter value too (`ClassDB::class_get_default_property_value`).

  The metadata dump records each property's getter return type where it differs from the registered one (`getterType`), and an attribute is typed by that getter type wherever the metadata has one, on a known class (with or without defaults) and on an element of unknown class alike, and by the declared type otherwise, so such a value is an attribute and converts back as Godot writes it, and a value written in the declared type still reads.
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

An `EditorExportPlugin`, registered by an `EditorPlugin` the project enables, loads each `.xscn` and `.xres` through the loader at export, saves Godot binary and adds it with `add_file(path, bytes, remap: true)`, so the exported game loads binary through Godot's `.remap` and runs no addon code at load. The raw XML is not shipped.

The loaders are packed too, as a fallback and for a `.xscn` autoload's type check (`main/main.cpp`, which asks `get_resource_type` of the original path, and that lookup does not follow a remap). The binary sits under `res://.godot/exported/godot-xaml/`, named as Godot names its own conversions, so a project holding both `a.xscn` and `a.scn` gets no clash.

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

Version 1 ships in four releases; the ones still to come are tracked as Linear's `vNext`, `vNext+1` and `vNext+2`:

1. **First release** (0.1.0), the one the owner's repos start trying: the core (model, parser, canonical writer), the `.tscn` conversion run on the owner's projects, the addon's loader, saver and export support, an install a project can follow, the CLI, and godot-mcp support for `.xscn`. A project can convert its scenes, edit them in Godot, ship a build, and let its agents preview and edit them.
2. **Second**: fixes and polish of what the first release ships, before any new feature: the conversion and the parser, the CLI's `convert` and `check`, the addon, the metadata dump and the docs, with the name checks against GodotSharp and the Godot comparison of resources.
3. **Third**: the language server and the VS Code extension.
4. **Fourth**: the Visual Studio extension and its preview pane, with the addon's preview server.

The later versions above sit in no release. Everything godot-xaml needs from godot-mcp is owner-approved at High priority there.

## Friction from projects using godot-xaml is filed as GitHub issues

From the first release, every repo that uses godot-xaml files each friction (a conversion that comes out wrong, a load error, an editor save that changes a scene it should not, a tooling gap) as a GitHub issue on `leftos/godot-xaml`, one per friction, naming the version, what happened against what was expected, and the workaround, as godot-mcp's friction rule does. The repo is public, so a private project's scene goes in as a minimal repro, never pasted whole.

## A project conversion is all or nothing, and backs up what it removes

`godot-xaml convert --project <root>` converts both ways: forward (`.tscn`/`.tres` to `.xscn`/`.xres`) by default, and back with `--back`, each rewriting references, scripts and `project.godot` the same way.

It skips `addons/` (third-party addon scenes stay as they are, and references to them are left alone), `.godot/`, every folder whose name starts with `.`, and every folder holding a `.gdignore` file. There is no flag to include `addons/`.

It always backs up what it removes or edits, unless the user passes `--nobackup`. The backup is a folder under the root, not the Recycle Bin, since the bin cannot meet "always": Windows silently deletes a file it cannot recycle and other systems have no bin.

It is all or nothing. The outputs are written to a hidden staging folder first. If any source fails, the staging folder is deleted and the project is left exactly as it was, with every error listed in that run. Only when every source converts are the originals backed up, the outputs moved in, the scripts and `project.godot` edited, and the originals removed. A failure after the first move is reported as a part-converted project, naming the backup to restore from; there is no automatic rollback.

## A project conversion leaves the files Godot loads before any addon as text

`convert --project` leaves as text the files named by `gui/theme/custom`, `gui/theme/custom_font`, `audio/buses/default_bus_layout`, `display/mouse_cursor/custom_image`, `rendering/environment/defaults/default_environment` and `internationalization/locale/translations`. For `audio/buses/default_bus_layout`, that is its default path `res://default_bus_layout.tres` unless an untagged key sets it, when that file exists.

It also leaves every `.tres`/`.tscn` those reach through ext_resources, and every file a left file's `.gd` or `.cs` script names by a `res://` or `uid://` string literal, transitively.

`.cs` is followed on purpose though a C# load runs after the addon's loaders exist: a wrongly left file only stays text, while a wrongly converted one breaks the game. Everything else converts.

A `uid://` value is resolved from the sources' headers, and a feature-tagged key counts. A `uid://` that names no file leaves nothing and prints nothing. The CLI does not read `override.cfg`. Each left file is named on an uncounted line, and the run neither converts, renames, backs up, removes nor rewrites references to it.

Why, from the Godot 4.7.2 source: Godot loads these files before `ResourceLoader::add_custom_loaders()` (`Main::setup2` in `main/main.cpp`, through its `initialize_theme`, `load_default_bus_layout` and `load_project_translations` calls, before `Main::start` calls `add_custom_loaders`), so the addon's loaders do not exist yet; and a left theme whose ext_resource was converted fails whole at startup (`ResourceLoaderText::_parse_ext_resource` in `scene/resources/resource_format_text.cpp`).

Whether a left file hits an export is unmeasured.

## A project conversion runs on one thread

`convert` stays serial: a conversion is a one-time step, so its speed does not justify the ordering and buffering parallel work would add to its output. A slow run is fixed by removing repeated work.

## A script-class `.xres` is found through a snapshot of the global classes

An `.xres` whose root `Script=` declares a global class (a GDScript `class_name` or a C# `[GlobalClass]`) answers `get_resource_script_class` with that class, and loads by the class name (`ResourceLoader.Load(path, "MyRes")`, `Exists`, `GetRecognizedExtensionsForType`), so the inspector's typed picker, typed Array and Dictionary drops and the FileSystem dock's script-class icon find it. The format does not change: `.xres` stores no `script_class`, and its resource type is still the native root class.

The loader answers from `GlobalClassSnapshot`, an immutable copy of the global class list built on the main thread and read from any thread, because the editor asks loaders for script classes from its scan thread and the live list is replaced on the main thread with no lock. It is rebuilt when the editor plugin enters the tree and on each `EditorFileSystem.script_classes_updated`, by `XresFormatLoader`'s constructor on the main thread, and by the game's explicit call to `XresFormatLoader.RefreshGlobalClasses()` after `ProjectSettings.LoadResourcePack` mounts a pack that declares global classes. The call is explicit because the engine gives no hook: a mount refreshes Godot's own list but re-creates no loader and emits no signal. The owner chose a public static call over documenting the limit or rebuilding automatically.

The constructor rebuild alone is not enough, from the Godot 4.7.2 source: at startup the loaders are built from the previous session's class cache (`main/main.cpp`, `Main::start`) and are not re-created after the first scan stores the fresh list (`editor/file_system/editor_file_system.cpp`: the first scan, the plugin load, then the threaded scan that asks a loader for a new `.xres`'s script class). A script deleted in the FileSystem dock emits `script_classes_updated` twice: the first, from `FileSystemDock::_resource_removed` (`editor/docks/filesystem_dock.cpp`), comes before the loaders are re-created, and the second, from `EditorFileSystem::_update_script_classes` (`editor/file_system/editor_file_system.cpp`), re-creates them. Between the two, only the plugin's hook keeps the snapshot right.

The plugin connects that hook by object and method name (`new Callable(this, MethodName.OnScriptClassesUpdated)`), not as a C# event. An assembly reload releases each C# delegate callable's handle before the domain reload and sets it again only after deserializing it (`modules/mono/csharp_script.cpp`, `CSharpLanguage::reload_assemblies`), so an emit in between logged `delegate_handle.value is null`, `Delegate::Invoke` and `script_classes_updated ... Method not found` errors, and the event's `-=` at exit no longer matched the restored connection. A by-name slot is skipped as invalid while the reload has the script removed (`core/variant/callable.cpp`, `Callable::is_valid`; `core/object/object.cpp`, `Object::emit_signalp`) and is called again once the reload gives the tool script a new instance. `_ExitTree` disconnects an equal callable when `IsConnected`.

## The editor reads scene groups by loading every `.xscn`

The addon documents the cost (`docs/guide/limitations.md`, "Projects") and adds no workaround, because Godot 4.7.2 leaves none.

From the source: `PackedScene::get_scene_groups` (`scene/resources/packed_scene.cpp`) reads a scene as text only when its extension is exactly `tscn`, answers from `ResourceCache` when a `PackedScene` is already loaded at the path, and otherwise calls `ResourceLoader::load`. `ResourceFormatLoader` has no virtual about groups (`core/io/resource_loader.h`), and the cache's `ProjectSettings` methods are not bound (`core/config/project_settings.cpp`), so the addon cannot answer groups cheaply or add entries to the in-memory map.

Rejected: writing `.godot/scene_groups_cache.cfg` from the plugin. `ProjectSettings` reads the file once at startup, before any plugin, and the next update rewrites it from memory, so the entries would be lost and Global Groups rename and remove would miss every scene. Rejected: returning a groups-only stub `PackedScene` from `_Load`. The engine's call is indistinguishable from any other load (`CACHE_MODE_REUSE`), and its result is cached at the path, so any load of that path in the frame would get the stub.

A loader hook proposed to Godot (a `ResourceFormatLoader` virtual that `get_scene_groups` asks before its load) would remove the cost.

## The addon's temp files live in a per-user private folder

The load and save routes write their converted text in the folder `AddonTempFolder` resolves: on Linux `$XDG_CACHE_HOME/godot-xaml` when that is absolute, else `$HOME/.cache/godot-xaml`; on macOS `~/Library/Caches/godot-xaml`; on Windows `%TEMP%\godot-xaml`, whose temp folder is already per user. On Unix the folder is created owner-only (mode 0700) and its mode is set as well, so a folder another account made is rejected. The folder is resolved on first use and a usable result is cached; an exported game, which has no project folder on disk, resolves it the same way.

When the folder cannot be used (no absolute `HOME`, a path inside the project, another account's folder, no write access), the addon uses a fresh per-process `godot-xaml-{random}` folder in the OS temp folder and warns once. The fallback is private to the process, so a load or save never fails over the temp folder. Clean-up stays the failed-load prune of files over a day old. The prune also removes Godot's backup-save leftovers of the save route's temp files under the same rule: with the editor's backup-save, `ResourceSaver.Save` writes a sibling of `{guid}.tscn` or `.tres` first and renames it at close, so a crash mid-save leaves it. The name is exactly 32 lowercase hex characters, `.tscn` or `.tres`, then `-` and six ASCII letters or digits (unix `mkstemp`) or digits and `.tmp` (Windows). Other names are left alone.

Rejected: the shared `/tmp`. Another account can create `/tmp/godot-xaml` first and block every load and save, or own it and read or swap the converted text between the addon's write and Godot's read. Rejected: `OS.GetCacheDir()`. On Windows it fills a function-local static without a lock on its first call (`platform/windows/os_windows.cpp`, `OS_Windows::get_cache_path`), and the base fallback is `"."` (`core/os/os.cpp`, `OS::get_cache_path`), which can land in the project.

The export plugin writes its temporary binaries in `res://.godot/exported/godot-xaml/`, where Godot saves its own export conversions, instead of the OS temp folder. The saver gets that file's absolute path: a `res://` path makes the editor's save callback write a folding file in `.godot/editor/` that is never removed, and a uid is minted for it.

## The addon disposes what it makes, not at shutdown

Every `Variant`, `StringName`, `NodePath` and Godot `Array` or `Dictionary` the addon makes is disposed in the call that made it, and a literal compared against as a `StringName` is a `static readonly` field. The addon installs no shutdown hook that disposes or collects them.

Godot fact (4.7.2): GodotSharp registers each of these in `DisposablesTracker.OtherInstances` under a weak reference and disposes at shutdown only what is still reachable (`modules/mono/glue/GodotSharp/GodotSharp/Core/DisposablesTracker.cs`, `OnGodotShuttingDown`). One a GC has found unreachable is left to its finalizer, and nothing drains the finalizers before `Main::cleanup` (`main/main.cpp`) tears the engine down, so a finalizer that runs late calls into a freed engine and can crash the process, with a native access violation or a `Fatal error.` line and a `*.Finalize()` stack.

Rejected: a shutdown hook that forces a GC and waits for the finalizers. It would add a full GC to every game's exit. Godot's generated dispatch leaves one `StringName` per C# loader per engine `_HandlesType` call, which is Godot's own code and outside the addon's reach; the check `LoadLeavesNothingToTheFinalizer` therefore calls each loader's `_HandlesType` directly.

## The editor checks fail on engine errors, not warnings

`godot/editor-checks.ps1` fails the run on any line of an editor's stderr starting `ERROR: `, `SCRIPT ERROR: ` or `SHADER ERROR: ` (case-sensitive), even when every check passed and the editor exited 0, and prints each as `ENGINE-ERROR editor-<mode> <kind> <line>`, `<kind>` one of `ERROR`, `SCRIPT-ERROR`, `SHADER-ERROR`. Warnings (`WARNING: `) stay informational and never fail the run.

Rejected: failing on `ERROR: ` alone. A script or shader error is as much a defect in the addon's editor behaviour as an engine error, and Godot prints it under its own prefix.

## The preview server

The open Godot editor's addon can listen on a local socket, render the `.xscn` text a client sends into a `SubViewport`, and send back an image and each node's rectangle, so the Visual Studio preview pane shows a scene as Godot draws it and a click in the image selects the element in the text. The text need not be saved: the server never touches the user's `.xscn`, and a request is rendered from the text it carries, with relative references resolved against the document's `res://` path.

**Lifetime.** The server is off until a project turns it on with the project setting `godot_xaml/preview_server/enabled`, default off, so no editor listens for a feature its user did not enable. The plugin starts the listener in `_EnterTree` and stops it in `_ExitTree`, and it must survive the C# assembly reload the plugin already survives (`XscnEditorPlugin`).

**Transport.** Loopback TCP. Each editor binds `127.0.0.1` on a free port, never all interfaces (`TCPServer.listen` in `doc/classes/TCPServer.xml` defaults to `*`), and writes `{port, token, pid, project}` to a discovery file under `%LOCALAPPDATA%\godot-xaml\editors\` (the platform's per-user local data folder elsewhere). The client picks the file for its project and sends the token; a connection without the token is refused. A stale file (pid gone, connect refused) means no editor is open. The file is removed when the server stops.

The listener uses the .NET runtime (`System.Net.Sockets`) on its own thread, since Godot has no named-pipe or Unix-socket class; every Godot call (scene, `SubViewport`, `ResourceLoader`) runs on the main thread.

**One server per editor.** Every request names its project, and a request for another project than the editor's is refused with a named error, so a second editor never answers for the first one's project.

**Protocol.** JSON-RPC 2.0 over a `Content-Length`-framed stream, as LSP frames it, written by hand with `System.Text.Json`. The image is a base64 PNG in the result. A superseded request is cancelled by a notification. Requests are latest-wins: the server keeps one pending request per document and drops older ones, so a keystroke stream never queues stale renders.

**Dependencies.** The addon takes no new NuGet dependency (no StreamJsonRpc): the codec and the server use only the .NET runtime (`System.Net.Sockets`, `System.Text.Json`), because the addon is copied beside one `PackageReference` and a new package would land in every user's game project.

**Image.** PNG. The request carries `width`, `height` and `scale`; the server clamps each side to 4096 pixels, so a client cannot ask for unbounded memory in the editor. Rectangles are in image pixels.

Godot facts (4.7.2): a `SubViewport` that is not on screen is not visible, so its `render_target_update_mode` is set to once rather than the default when visible; the texture is only right after `RenderingServer.frame_post_draw` (`doc/classes/Viewport.xml`), and `Image.save_png_to_buffer` encodes without a file. Headless Godot cannot produce an image: the dummy renderer's `render_target_get_texture` returns an empty RID (`servers/rendering/dummy/storage/texture_storage.h`), so only a windowed editor renders pixels.

**Scripts.** Scripts are stripped from the previewed scene, with the exported property values kept where the node class has them, unless the request asks for scripts and the project setting allows it; the default is off. Stripping (`XscnPreviewScene.WithoutScripts`, on the converted text before the load) removes every `script` property that names a script (an explicit `Script="{x:Null}"` stays), every Script-class sub_resource and ext_resource (built-in scripts included) and anything a removed `script` pointed to, sets every reference to a removed resource to null, and drops each connection whose From or To node lost its script; every other property stays, which is how the exported values survive. Connection paths are compared after Godot's own simplification (`.` dropped, `name/..` collapsed).

The allowing setting is `godot_xaml/preview_server/allow_scripts`. A scene the text instances still loads its own scripts; the owner's ruling is to strip those too (GXAML-177). A socket that hands text to an in-editor instantiate would otherwise run any `[Tool]` script class of the project, with the text choosing which and with which properties (a C# class without `[Tool]` is a placeholder in the editor; `modules/mono/csharp_script.h`, `is_tool`).

**Errors.** When the text does not parse or convert, the server returns the last good image for that document, marked stale, with the new diagnostics. It holds the last good rendering per document, and returns diagnostics in the response rather than pushing them to Godot's log, which a keystroke stream would flood. A document with no good rendering yet returns the diagnostics and no image.

**Rectangles and spans.** Rectangles are for Controls in the first version (`Control.get_global_rect`), each entry carrying a `kind` field (`control`), so 2D and 3D entries can follow without a protocol break. The server parses the received text with the core, tags each instantiated node with its element's `XscnElement.Span` (UTF-16 offsets), and returns `{path, class, kind, rect, spanStart, spanLength}` per node, back to front, so the client picks the topmost hit. `path` is relative to the scene root.

A node inside an instanced scene has no element of its own, so it selects the instance's element. Each response carries the text version it rendered, and a client ignores a click on a rendering older than its text. A rotated or skewed Control has a `get_global_rect` that Godot calls not meaningful (`doc/classes/Control.xml`).

**Tests.** The codec, token check, project refusal, text-to-scene instantiation and rectangle structure (containment, ordering, entry count, paths and classes, never values: headless text metrics are not real) run headless in `godot/checks.ps1`, in plain C# in the addon folder. Pixels, the discovery file's life, a request's PNG size and a hit-test run in `godot/editor-checks.ps1`, on a desktop.

## Name

The repo is `godot-xaml`, public at `github.com/leftos/godot-xaml`, MIT. Scenes are `.xscn`, resources `.xres`.
