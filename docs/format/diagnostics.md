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
- a format element other than `x:Instance` as the root.

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

Warning. A resource with a script whose global class name the host could not give, so the `.tres` header gets no `script_class=`. The CLI has no editor to ask; the editor's loader gives it on the main thread.

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

Warning. The class of an instanced scene could not be read: no lookup was given, or the file is unparsable, a cycle, missing the node path, or referred to by `uid://` only. The section keeps its written property order rather than Godot's.

### GXC0119 Property name XML cannot hold

Error. `The property name "metadata/a\u0001b" holds a character XML cannot hold; the property is dropped.` A `.tscn` or `.tres` names a property whose name holds a control character, U+FFFE, U+FFFF or a lone surrogate, which neither an attribute nor an `x:Prop` name can hold. Rename the property in Godot; a name holding a control character cannot be written as XML.

### GXC0120 Uids disagree

Warning. `x:Uids gives 'res://a.tres' more than one uid (uid://bbb, uid://aaa); its ext_resource keeps 'uid://bbb'.` Two `x:Uid` entries name one path with different uids, written so or made so when `convert` rewrites one file's path onto another's. The `.tscn` keeps the first uid; check which file the path should name, and remove the other entry.

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
