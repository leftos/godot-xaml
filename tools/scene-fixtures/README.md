# scene-fixtures

Writes `tests/fixtures/scenes/`: `.tscn` and `.tres` files saved by Godot 4.7's own text saver (`ResourceSaver.save`). The core's scene reader and writer are tested against these files, so every expected byte comes from Godot, never from hand.

## Run

```
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/scene-fixtures.log --timeout 300 --slot light -- pwsh -NoProfile -File tools/scene-fixtures/generate.ps1 -Godot F:/Godot/Godot_console.exe
```

`generate.ps1` takes `-Godot` (else `$env:GODOT_BIN`) and an optional `-OutDir` (default `tests/fixtures/scenes/`), runs `generate.gd` headless over this folder's project, prints `scene-fixtures: wrote N files` and exits with Godot's status.

## Regenerating is a deliberate act

Ext-resource and sub-resource ids are pinned (`Resource.set_id_for_path`, `resource_scene_unique_id`), so they are the same on every run. Node `unique_id` values are not: the packer draws them at random (`packed_scene.cpp`, `SceneState::_parse_node`) and `Node.set_unique_scene_id` is not exposed to scripts. Every run therefore rewrites the `unique_id=` numbers of every scene, and nothing else.

Regenerate only when a case is added or changed. A changed case commits the whole folder after the run; a new case is copied alone from an `-OutDir` run, so the other fixtures keep their `unique_id`s. A new fixture also goes in the three fixture lists (`SceneForwardTests.Scenes`, `SceneBackTests.Fixtures`, `SceneRoundTripTests.Fixtures`), `SceneBackTests.Inputs`, `ExpectedPairs` in `godot/compare/Compare.cs` and `$expectedFixtures` in `godot/compare.ps1`.

## Files

- `generate.gd`: the generator (extends `SceneTree`, works in `_initialize`). Each case builds its scene or resource in code, saves it under `res://cases/` (ignored by git; cases reference each other there by `res://` path) and copies the saved bytes unchanged to the output folder.
- `holder.gd`: a node script with an exported `Node` property, which gives `node_paths=`.
- `tagged.gd`: a resource script with `class_name`, which gives `script_class=`.
- `quoted.gd`: a resource script whose property names `property_name_encode` must quote.

## Cases

- `lone_root.tscn`: one node.
- `nested.tscn`: children with parent paths and a property.
- `unique_name.tscn`: `unique_name_in_owner`.
- `groups.tscn`: two groups, which the saver sorts.
- `script_node.tscn`: a script ext resource and `node_paths=`.
- `material.tres`: a plain resource.
- `gradient_texture.tres`: a resource with a sub-resource.
- `sub_resources.tscn`: a sub-resource chain used by one node and a sub-resource shared by two.
- `ext_resource.tscn`: another case's `.tres` as an ext resource.
- `instanced.tscn`: an instance of `nested.tscn` with properties overridden on it and on a node inside it, a child added inside it (`index=`) and editable children (`[editable]`).
- `inherited.tscn`: a scene inheriting `nested.tscn`. Scene inheritance cannot be set up from a script, so the generator writes a one-node seed scene that inherits `nested.tscn`, loads it, edits it and saves the result; the fixture is the saver's output.
- `connections.tscn`: connections with default flags, deferred, one-shot, binds and unbinds.
- `script_class.tres`: a resource with a global script class.
- `packed_bytes.tres`: a PackedByteArray over 64 bytes, which makes the file `format=4`.
- `quoted_name.tres`: property names with a space, `=`, `"` and a non-ASCII character.
- `placeholder.tscn`: `instance_placeholder=`.
- `multiline.tscn`: a multi-line string and a dictionary inside a node section.
- `property_order.tscn`: properties whose result depends on the order they are set in: `grow_horizontal` and `grow_vertical` with `layout_mode`, `button_pressed` with `toggle_mode`.
- `order_inner.tscn`: the scene `instanced_order` instances: a `Control` root and a `Panel` child at `layout_mode = 1`, so an override that sets 0 is saved.
- `instanced_order.tscn`: `layout_mode = 0` and `grow_vertical = 2` on an instance root and, through an override, on its child; in alphabetical order the grow direction is reset, so it proves the way back reads the instanced scene's classes.

`uid=` attributes are absent from every fixture: the saver asks the editor's file system for uids, which a `--script` run does not start.
