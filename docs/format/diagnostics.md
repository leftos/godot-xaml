# Diagnostics

Every error and warning godot-xaml reports, by code. A code never changes meaning once released.

| Prefix | Who reports it |
|---|---|
| `GX` | The parser, on reading an `.xscn` or `.xres`: `godot-xaml check`, the Godot loader, and every other tool. |
| `GXC` | The conversion between `.xscn`/`.xres` and `.tscn`/`.tres`: `godot-xaml convert`, the editor's loader and saver. |
| `GXP` | `godot-xaml convert`, about the project around the files it converts. |
| `GXT` | `godot-xaml`, about one file it cannot read or write. |

Severities:

- **An error in a `GX` or `GXC` code** means the file cannot be used as it is. Godot's load fails with the message, and `convert` does not write the file.
- **A warning** means the file loads, or is written, with something not as the source had it.
- **`GXP` codes are always warnings.** They do not change `convert`'s exit code.

The CLI prints one line per problem, in the form editors and CI read:

```text
ui/main.xscn(2,4): error GX0104: <Label> has no Name; every node needs one.
```

## Parser: `GX`

### GX0001 Not well-formed XML

Error. The text is not well-formed XML: an unclosed tag, a missing quote, a stray `<`. The message is the XML parser's own. Fix the XML at the position given.

### GX0002 Comment inside a start tag

Error. `A comment cannot sit inside a start tag; move it before the element.` XML allows a comment only between elements. Move it before the element it describes, where it belongs to that element.

### GX0003 Unexpected text

Error. `Only elements and comments belong here.` Text sits between elements; values belong in attributes.

### GX0004 No root element

Error. `The document has no root element.` The file is empty, or holds only comments.

### GX0101 Unknown x: element

Error. `<x:Name> is not an element of the format.` The format's elements are `x:Uids` (and `x:Uid` in it), `x:Resources`, `x:Instance`, `x:Override`, `x:Connect` and `x:Prop`. Check the spelling.

### GX0102 Unknown x: attribute

Error. `<tag> takes no attribute x:Name.` The attribute is not one the format defines, or this element cannot carry it, such as `x:Key` outside `x:Resources`, or an attribute on `x:Uids` or `x:Resources`. The `x:` attributes are listed in [the format reference](README.md#x-attributes-on-a-node).

### GX0103 Undeclared prefix

Error. `The prefix 'p' is not declared; declare xmlns:p on the root.` A tag or attribute uses a prefix the root does not bind. Add `xmlns:p="res://folder/"` to the root for a scene folder, or fix the prefix.

### GX0104 Node without a name

Error. `<tag> has no Name; every node needs one.` A node has no `Name`, an empty one, or `Name="%"`. Give it a name.

### GX0105 Invalid value

Error. A value is not text of its property's type. The message says what was expected:

- `Expected True or False.`
- `Expected a number.`
- `Expected an integer.`
- `Expected an integer that fits in 32 bits.`
- `Expected 2 numbers separated by commas.`
- `Expected 2 integers separated by commas.`
- `Expected a res:// path, {Key} or {x:Null}.`
- `Expected Godot value text: …`
- `Expected names joined with commas, or a Godot array of strings.`
- `ExtResource(…) is not used in .xscn; refer to a file as Resource("res://…").`

A signal-named attribute that is a property, such as `Pressed` on an instance, an override or an unknown class, adds `To connect a signal here, use <x:Connect Signal="Pressed" Method="OnPressed" />.` The attribute is still a property; write the signal as `x:Connect`.

Write the value in the form [values.md](values.md) gives for its type.

### GX0106 Unknown enum member

Error. `Expected one of …, or an integer.` An enum or flags property names a member its type does not have. The message lists the members; flags may join several with `, `.

### GX0107 Unknown class

Warning. `No Godot class is named Name; the element is kept with an unknown type.` The tag is not a class the metadata knows. It may be a GDExtension class, which loads; otherwise check the spelling, using the class's C# name (`CpuParticles2D`). The Godot addon does not report it for a class the running engine has, such as a GDExtension class; `godot-xaml check` does.

### GX0108 Missing attribute

Error. An element lacks an attribute it needs:

- `<x:Instance> needs Scene="res://…".`
- `<x:Override> needs Path="…", the node's path inside the instance.`
- `<tag> in x:Resources needs x:Key.`
- `<StyleBoxFlat> in x:Resources has an empty x:Key; every entry needs one.` The message spans the whole `x:Key` attribute. The entry is not read, as with `GX0116`, so two empty keys give two `GX0108` and no `GX0116`.
- `<x:Connect> needs Signal="…" or GodotSignal="…".`
- `<x:Uid>` needs `Path` and `Value`; `<x:Prop>` needs `Name` and `Value`; `<x:Connect>` needs `Method`.

### GX0109 Misplaced namespace declaration

Error. `Namespace declarations belong on the root element.` Move the `xmlns:` declaration to the root.

### GX0110 Invalid namespace

Error. The declaration is not one the format accepts:

- a default `xmlns` (`A default namespace is not used: Godot classes are tags without a prefix.`);
- a prefix bound to something other than a `res://` folder ending in `/` or the format's own URI;
- a second prefix bound to the format's URI (`The format namespace is already bound to the prefix 'x'; xmlns:y is not used.`). The first binding stays the format prefix, and an element or attribute using the second prefix reports `GX0103`.

### GX0111 Misplaced element

Error. An element sits where the format does not allow it. The cases:

- `x:Uids` or `x:Resources` below the root;
- a node or `x:Connect` inside a resource or an `.xres`;
- an element inside `x:Uid`, `x:Prop` or `x:Connect`;
- something other than `x:Uid` in `x:Uids`;
- something other than a resource in `x:Resources`;
- a format element other than `x:Instance` as the root;
- `x:Scene` anywhere but a scene's root node;
- a second scene root inside a `PackedScene`.

The message names the case.

### GX0112 Invalid property element

Error. A property element (`<Owner.Property>`) does not begin with its parent's own tag, or does not hold exactly one resource element. Rename it after the parent's tag, as `<Button.Theme>` inside `<Button>`, and give it one resource.

A property element with an empty name or an empty dotted segment (`<Control.>`, `<Control.Theme.>`, `<Control..a>`) reports `<Control.> names no property; write <Control.Property>.` at the tag name, and adds no property.

### GX0113 Repeated attribute

Error. `The attribute Name is already set on this element; the first value is kept.` XML forbids an attribute twice on one element. Remove one.

### GX0114 Scene prefix on an attribute

Error. `The prefix 'p' binds a scene folder, which names elements, not attributes; Name is not read.` Scene prefixes name instanced scenes, so remove the prefix from the attribute.

### GX0115 Second table

Error. `Only one <x:Uids> table is allowed; this one is not read.` The same holds for `x:Resources`. Merge the two tables.

### GX0116 Repeated key

Error. `The key 'Key' is already used in x:Resources; this resource is not read.` Give each entry its own key.

### GX0117 Not a resource

Error. `<tag> is a Class, not a Resource; only a resource can be held here.` The element sits inside a property element, in `x:Resources` or at the root of an `.xres`, where only resources belong, but its class is not a Resource.

### GX0118 Signal and GodotSignal

Error. `<x:Connect> has both Signal and GodotSignal; write one.` Keep `Signal` (the C# name) unless it would read back as another signal, then keep `GodotSignal` (Godot's name).

### GX0119 Placeholder at the root

Error. `The root cannot be an instance placeholder; Godot loads no scene whose first node is one.` Godot refuses a scene whose first node is an instance placeholder, because the scene cannot be used for inheritance. Make the root a node of the scene's own, and put the placeholder under it.

### GX0120 Nested too deep

Error. `Elements are nested deeper than 512; the reader stops here.` A deeper file would exhaust the call stack of every program that reads it, and no Godot scene needs that depth. Flatten the tree, or split it into instanced scenes.

### GX0121 Malformed x:Godot

Error. `x:Godot is "4"; it names the Godot version as <major>.<minor>, such as 4.7.` The root's `x:Godot` names the Godot version whose metadata wrote the file, as its major and minor numbers joined by a dot. The marker is not kept. Correct it, such as `x:Godot="4.7"`, or remove it: a file without one reads as before, and the next save writes it.

### GX0122 Needs a newer Godot

Error. `x:Scene needs Godot 4.8 or later; this file is read with Godot 4.7's metadata`, or `a scene inside PackedScene needs Godot 4.8 or later; this file is read with Godot 4.7's metadata`. The file uses a construct that only Godot 4.8 or later can load: an `x:Scene` element, or a scene root inside a `PackedScene` resource. The file is read with an older Godot version's metadata, from its `x:Godot` marker or, unmarked, from the Godot that loads it. Remove the construct, or move the project to Godot 4.8 with `godot-xaml upgrade`.

### GX0123 Second x:Scene

Error. `a second x:Scene; a scene has one` A scene's root node holds at most one `x:Scene` element. Merge the two into one.

## Conversion to `.xscn`: `GXC00xx`

Reported when a `.tscn` or `.tres` is converted forward, by `godot-xaml convert` and by the editor's saver.

### GXC0001 Unrepresentable attribute

Error. A section attribute the `.xscn` has no place for. The file is not written; this happens only with text Godot did not write.

### GXC0002 Unknown ext_resource id

Error. `ExtResource("id") names no [ext_resource].` The reference is broken in the source. Fix the `.tscn`, or re-save it in Godot.

### GXC0003 Unknown sub_resource id

Error. `SubResource("id") names no [sub_resource].` As for GXC0002.

### GXC0004 Unused ext_resource

Warning. An `[ext_resource]` nothing refers to, which Godot never writes; it is dropped.

### GXC0005 Unused sub_resource

Warning. A `[sub_resource]` nothing refers to, which Godot never writes; it is kept as an `x:Resources` entry.

### GXC0006 Sub-resource cycle

Error. A `[sub_resource]` refers back to itself through others; it is kept as an `x:Resources` entry.

### GXC0007 Duplicate id

Error. `An earlier section already has the id 'id'; this one is not converted.`

### GXC0008 Unknown class

Warning. `The metadata has no class 'Name'; it is kept by name.` Usually a GDExtension class; the element is written with that tag.

### GXC0009 Node parent not found

Error. A `[node]` whose `parent=` names no earlier node, a node after the first with no `parent=`, or a node with neither `type` nor `instance` outside any instance. It and its subtree are not converted.

### GXC0010 Instance is not a scene

Error. An `instance=` that is not `ExtResource("id")` of an existing section. The node and its subtree are not converted, and a `Node` stands in for a root.

### GXC0011 Repeated sibling name

Error. A second child of one parent with the same name; the later one is not converted.

### GXC0012 Connection emitter not found

Error. A `[connection]` whose `from` names no node; it is not converted.

### GXC0013 Editable path not found

Error. `path="p" names no node; the [editable] is not converted.`

### GXC0014 Root not convertible

Error. A scene with no `[node]`, or whose first node has neither a type nor a scene; a `Node` stands in for the root.

### GXC0015 Instance and placeholder

Warning. A node with both `instance=` and `instance_placeholder=`. Godot loads it as a placeholder, so `instance=` is dropped.

## Conversion to `.tscn`: `GXC01xx`

Reported when an `.xscn` or `.xres` is converted back, by `godot-xaml convert --back` and by the editor's loader on every load.

### GXC0101 Unknown ext_resource type

Warning. A referenced file whose class nothing gives: not the editor, not the file's extension, and not the property using it. Its `[ext_resource]` says `type="Resource"`, which Godot reads correctly in most cases.

### GXC0102 Unknown key

Error. `No x:Resources entry has the key 'Key'.` A `{Key}` or `SubResource("Key")` names no entry. A bare reference is dropped, and one in value text is kept. Add the entry or fix the key.

### GXC0103 Invalid value

Error. A value the parser could not read, already reported as a `GX` code; the property is dropped.

### GXC0104 ExtResource in value text

Error. An `ExtResource(…)` inside value text names no section in an `.xscn`. Write `Resource("res://…")` instead.

### GXC0105 Unused x:Resources entry

Warning. An entry nothing refers to, which Godot would not save; it is dropped.

### GXC0106 Script class unknown

Warning. A resource with a script whose global class name the host could not give, so the `.tres` header gets no `script_class=`. The CLI reads the project's global class cache (`.godot/global_script_class_cache.cfg`, written by the editor's scan or `godot --headless --import`) and gives this warning when the project has none, it cannot be read, or the source is in no project; the editor's loader gives the class on the main thread.

### GXC0107 Not a resource

Error. A resource whose tag names a scene, or a resource document whose root is a node; it is written as `Resource`.

### GXC0108 Resource cycle

Error. A resource refers back to itself through others. The reference that closes the loop is not followed.

### GXC0109 Missing key

Error. `This x:Resources entry has no x:Key; it is dropped.`

### GXC0110 Repeated key

Error. An entry whose key an earlier entry has; the later entry is dropped.

### GXC0111 Uid without a path

Error. A `Resource("uid://…")` with no path, which an `[ext_resource]` cannot be written for. Write `Resource("uid://…", "res://…")`.

### GXC0112 Scene root is not a node

Error. A scene document whose root is a resource; no `[node]` is written. Use `.xres` for a resource.

### GXC0113 Override outside an instance

Error. An `x:Override` that is not a direct child of an instance or the inherited root. Its path is taken under its parent; move it up and give it the full path from the instance.

### GXC0114 Override without a usable path

Error. An `x:Override` with no `Path`, or with an empty, `.` or `..` segment, names no node. It and what it holds are not written.

### GXC0115 Unique name conflict

Warning. A node named unique (`Name="%X"`) whose `UniqueNameInOwner` is set false; it is written unique.

### GXC0116 Placeholder type dropped

Warning. A placeholder (`x:InstancePlaceholder`) whose tag names a class other than `InstancePlaceholder`, or a scene; the tag is dropped.

### GXC0117 Comment dropped

Warning. A save could not carry a comment of the file it replaced, because what the comment preceded is gone. The message names it.

### GXC0118 Instanced class unreadable

Warning. The class of an instanced scene could not be read: the file is missing, unparsable, a cycle, missing the node path, or referred to by `uid://` only. The section keeps its written property order rather than Godot's.

### GXC0119 Property name XML cannot hold

Error. `The property name "metadata/a\u0001b" holds a character XML cannot hold; the property is dropped.` A `.tscn` or `.tres` names a property whose name holds a control character, U+FFFE, U+FFFF or a lone surrogate, which neither an attribute nor an `x:Prop` name can hold. Rename the property in Godot; a name holding a control character cannot be written as XML.

### GXC0120 Uids disagree

Warning. `x:Uids gives 'res://a.tres' more than one uid (uid://bbb, uid://aaa); its ext_resource keeps 'uid://bbb'.` Two `x:Uid` entries name one path with different uids, written so or made so when `convert` rewrites one file's path onto another's. The `.tscn` keeps the first uid; check which file the path should name, and remove the other entry.

### GXC0121 Instance not stripped

Error. `res://ui/legacy.scn: a binary scene cannot be previewed with scripts off`.

With scripts off, the preview strips every scene and text resource the text references (an instance, an inherited root, or a scene in a property or metadata value), at any depth, before loading it, and this one could not be stripped: it is a binary scene (a `.scn`, or a `.res` holding a scene or whose class cannot be read), it is a binary resource, it holds a scene but is not a `.tscn` or `.xscn` (`<path> is not a text scene`), or it cannot be read, does not parse or does not convert.

The preview shows no scene.

Fix the referenced scene (save a binary `.scn` as `.tscn` or `.xscn`), or allow scripts for the preview: the request asks for them and the project setting `godot_xaml/preview_server/allow_scripts` is on.

An imported model (`.glb`, `.gltf`, `.fbx`, `.blend`, `.dae`, `.obj`, or another file the editor imports as a scene) gives `<path>: an imported scene cannot be previewed with scripts off; allow scripts to preview it`; allowing scripts is the only way to preview it.

A referenced `.tres` or `.xres` is stripped the same way, and one that cannot be read, does not parse or does not convert says so as a resource: `<path>: the resource cannot be read: …`, `… does not parse: …`, `… does not convert: …`. A binary resource that is not a scene (a `.res`) gives `<path> is a binary resource, which the preview cannot strip of scripts; save it as .tres or turn on godot_xaml/preview_server/allow_scripts`.

A file whose kind the preview cannot prove inert is refused too, with a message naming the path and the reason and ending `; turn on godot_xaml/preview_server/allow_scripts to preview it`.

Such a file is a file with an extension none of Godot's own loaders claims (missing or not, since a project loader script may claim it), a `.gdextension` native library, a remap with an unsafe target (the message names the target), a `.import` sidecar that names a script or cannot be read safely, and an imported asset that is not a binary resource under `res://.godot/imported/` (`<source> is imported to <target>: …`).

A project that declares a global class whose native base is `ResourceFormatLoader` is refused whole, one error per class, located at its script: `<class> is a ResourceFormatLoader class, which Godot registers as a loader that a failed load hands files to; turn on godot_xaml/preview_server/allow_scripts to preview it`. The addon's own two loaders are exempt.

An instance placeholder (`x:InstancePlaceholder`) is loaded as a scene by the editor's preview, so its path is checked like an instance's.

A script gives `<path> is a script that an instance placeholder would load; turn on godot_xaml/preview_server/allow_scripts to preview it`, a text resource `<path> is a text resource that an instance placeholder would load as a scene; turn on godot_xaml/preview_server/allow_scripts to preview it`, and a scene file that holds a script resource is refused too. A missing `.tscn` gives the read failure, as a missing instance does.

A `Resource("…")` value in a referenced `.tscn` or `.tres`, or inside an `Object(...)` value, that names a file the preview cannot redirect to a stripped copy gives `<path> is referenced by a Resource() value, which the preview cannot redirect to a stripped copy`, or the message of the file's own kind.

### GXC0122 Source file unreadable

Warning. `The file "res://s.gdshader" could not be read (not found), so this section keeps its written property order.` An external `.gdshader` or mesh file that a by-source family is ordered by could not be read: `not found` when the host cannot give it, or `line L, column C: <reason>` when a mesh file does not parse. Reported once per file and reason, at the first section that meets it, which keeps its written property order rather than Godot's. Silent with no lookup or no source key, for a shader only the preprocessor could read, and for a file that is not an ArrayMesh or holds no blend shape names.

### GXC0123 Needs a newer Godot

Error. `this file holds a Godot 4.8 scene [resource] section or embedded scene, which Godot 4.7 cannot load` A `.tscn` or `.tres` saved by Godot 4.8 holds a scene's own properties in a `[resource]` section before its first node, or a PackedScene sub-resource with nodes of its own, and the conversion targets an older Godot, whose loader reads neither. The file is not converted, so nothing in it is dropped. Convert it for Godot 4.8 or later, or save the scene again in the older Godot.

## Project conversion: `GXP`

Reported by `godot-xaml convert`; the [CLI reference](../../src/GodotXaml.Cli/README.md#convert) has every message.

### GXP0001 No project root

Warning. A source with no `project.godot` above it, so its `res://` references are not rewritten.

### GXP0002 Untouched reference

Warning. A file the run does not write refers to a file the run converts: a scene, a resource, a script string or `project.godot`. The message says whether the reference carries a uid, which keeps it resolving once the source is removed.

### GXP0003 Shared uid

Warning. A kept source still carries the uid its output now also carries. Remove the source.

### GXP0004 Unrewritten path

Warning. Text holding a converted path that the rewriter could not change, such as a path inside a value it could not parse.

### GXP0005 Failed write

Warning. A written file names the output of a file this run could not write.

### GXP0006 Built path

Warning. A script or `project.godot` string holds a converted path inside longer text, or ends in an extension the run converts, so a path built from it at runtime is not rewritten. Change the script by hand.

### GXP0007 Originals kept

Warning. `nothing changed: n source(s) were not converted; the project is as it was` A `--project` run had a source that failed, so nothing in the project changed. Fix the errors above it and run again.

### GXP0008 Not UTF-8

Warning. A script or `project.godot` of a `--project` run is not UTF-8 text, so it is neither scanned nor edited.

## File problems: `GXT`

### GXT0001 Unreadable source

Error. A `.tscn` or `.tres` the reader cannot read, with Godot's own error text and its line and column. The file is not converted.

### GXT0002 Refused document

Error. The conversion or the writer refuses the document. The file is not written.

### GXT0003 Wrong kind of file

Error. A `.tres` holding a scene, or a `.tscn` holding a resource. The file is not converted.

### GXT0004 Version not carried

Warning. `written with Godot 4.8's metadata, which this tool does not carry; reading it with 4.7's` A file's `x:Godot` marker names a Godot version whose metadata this tool does not carry, so the file is read with the version its project names, else the newest version it carries. One line is printed per marker value in a run, at the first file that carries it.

### GXT0005 Not written

Error. `written with Godot 4.8's metadata, which this tool does not carry (it has 4.7's); not written, so the file keeps 4.8's typing and marker` `format` refuses to write a file whose marker names a version the tool does not carry, since writing would restamp it with the metadata of the version the tool has. The file is left as it is.

### GXT0006 Marker disagrees with --from

Error. `x:Godot is 4.6 but --from is 4.7` An `upgrade` reads a marked file with the version its `x:Godot` marker names, so a `--from` that names another version is a mistake about the file. Nothing in the project is upgraded.

### GXT0007 No marker

Error. `no x:Godot marker; pass --from with the version it was written with` An `upgrade` met a file with no `x:Godot` marker and was given no `--from`, so it does not know which version's metadata the file was written with. Nothing in the project is upgraded.

### GXT0008 Property removed

Warning. `Label.legacy_text is not in Godot 4.7's metadata; kept as a script property with its value text` The version an `upgrade` writes with no longer has a property the file sets, so the property is written as a script property: an attribute under its Godot name holding its Godot value text (`legacy_text='"old"'`). One line per class and property in a file.

### GXT0009 Project version not carried

Warning. `Godot 4.9 has no bundled metadata; reading its unmarked files with 4.8` A file with no `x:Godot` marker is read with the Godot version its project's `project.godot` names in `config/features`. When the tool does not carry that version, or the `project.godot` cannot be read, the project's unmarked files are read with the newest version the tool carries. One line is printed per project in a run, naming its `project.godot`. A project that names no version reads with the newest version with no warning.
