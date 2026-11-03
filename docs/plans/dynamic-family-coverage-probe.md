# GXAML-127 probe results: what a headless Godot 4.7.2 run stores

Run: `pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/probe-127/run.log --slot light --timeout 300 -- F:/Godot/Godot_console.exe --headless --script .tmp/probe-127/probe.gd` (passed, 1 s). Engine: `Godot Engine v4.7.2.stable.mono.official.ed1daf0bf`. Script: `.tmp/probe-127/probe.gd`. Saved files: `.tmp/probe-127/out/`. Full output: `.tmp/probe-127/run.log`. No ERROR or WARNING line appears anywhere in the log.

The run is projectless (the worktree root has no `project.godot`; the Godot project lives in `godot/`), so `res://` resolved to the worktree root and the probe wrote under `.tmp/probe-127/out/`. The rendering driver is the headless dummy, the same one the fixture generator runs under.

| family | stored without a tree | stored in a tree | the API calls that stored it | Godot errors seen |
|---|---|---|---|---|
| 1. GeometryInstance3D `instance_shader_parameters/` | yes | yes | `MeshInstance3D.mesh = BoxMesh.new()`; `material_override` = ShaderMaterial whose `Shader.code` is `shader_type spatial; instance uniform float probe_value = 0.0;`; `MeshInstance3D.set_instance_shader_parameter("probe_value", 2.5)` | none |
| 2. CanvasItem `instance_shader_parameters/` | yes | yes | `ColorRect.material` = ShaderMaterial whose `Shader.code` is `shader_type canvas_item; instance uniform float probe_value = 0.0;`; `CanvasItem.set_instance_shader_parameter("probe_value", 2.5)` | none |
| 3. ShaderMaterial `shader_parameter/` (`.tres`) | yes | n/a (resource, not a node) | `ShaderMaterial.shader` = Shader with `uniform float probe_value = 0.0; uniform vec4 probe_color : source_color;`; `set_shader_parameter("probe_value", 2.5)`; `set_shader_parameter("probe_color", Color(1, 0, 0))`; `ResourceSaver.save(material, "shader_material.tres")` | none |
| 4. ShaderGlobalsOverride `params/` | yes | yes | `RenderingServer.global_shader_parameter_add("probe_global", RenderingServer.GLOBAL_VAR_TYPE_FLOAT, 1.0)` (succeeded; `global_shader_parameter_get_list()` then returned `[&"probe_global"]`); `ShaderGlobalsOverride.set("params/probe_global", 3.0)` | none |
| 5. GridMap `baked_meshes` | yes | yes | `MeshLibrary.create_item(0)`, `set_item_name(0, "Box")`, `set_item_mesh(0, BoxMesh.new())`; `GridMap.mesh_library = library`; `set_cell_item(Vector3i(0..2, 0, 0), 0)`; `GridMap.make_baked_meshes()` (no arguments) | none |
| 6. AudioStreamPlayer `parameters/` | attempt 1 no, attempt 2 yes | attempt 1 no, attempt 2 yes | attempt 2: `player.stream = AudioStreamOggVorbis.new()`; `player.set("parameters/looping", true)` | none |
| 7. AudioStreamPlayer2D `parameters/` | attempt 1 no, attempt 2 yes | attempt 1 no, attempt 2 yes | attempt 2: same as row 6 | none |
| 8. AudioStreamPlayer3D `parameters/` | attempt 1 no, attempt 2 yes | attempt 1 no, attempt 2 yes | attempt 2: same as row 6 | none |

## The saved lines that prove each stored

Family 1 (`out/geometry_instance_3d.tscn` and `out/geometry_instance_3d_in_tree.tscn`, both line 16):

    instance_shader_parameters/probe_value = 2.5

Family 2 (`out/canvas_item.tscn` and `out/canvas_item_in_tree.tscn`, both line 13):

    instance_shader_parameters/probe_value = 2.5

Family 3 (`out/shader_material.tres`, lines 12-13):

    shader_parameter/probe_value = 2.5
    shader_parameter/probe_color = Color(1, 0, 0, 1)

Family 4 (`out/shader_globals_override.tscn` and `out/shader_globals_override_in_tree.tscn`, both line 4):

    params/probe_global = 3.0

Family 5 (`out/grid_map.tscn` line 29, `out/grid_map_in_tree.tscn` line 29):

    baked_meshes = [SubResource("ArrayMesh_656ef")]
    baked_meshes = [SubResource("ArrayMesh_n8k8a")]

Families 6, 7, 8 attempt 2 (`out/audio_stream_player_attempt2.tscn`, `out/audio_stream_player_2d_attempt2.tscn`, `out/audio_stream_player_3d_attempt2.tscn` and their `_in_tree` files, each line 6-7):

    stream = SubResource("AudioStreamOggVorbis_ilnwb")
    parameters/looping = true

Attempt 1 wrote the stream but no `parameters/` line, for example `out/audio_stream_player.tscn` line 12: `stream = SubResource("AudioStreamInteractive_y2smh")`, with no `parameters/switch_to_clip` anywhere in the file.

## Why the two audio attempts differ (source facts, Godot 4.7.2)

- `AudioStreamInteractive::get_parameter_list` (`modules/interactive_music/audio_stream_interactive.cpp:452-459`) pushes its one parameter, `switch_to_clip`, with `PROPERTY_USAGE_EDITOR` alone: no STORAGE bit, so a player's `parameters/switch_to_clip` is never serialised whatever value it holds. This is a property of that class, not of the family; the class is a dead end for a compare fixture.
- `AudioStreamOggVorbis::get_parameter_list` (`modules/vorbis/audio_stream_ogg_vorbis.cpp:565-567`) and `AudioStreamMP3::get_parameter_list` (`modules/mp3/audio_stream_mp3.cpp:271-273`) push `looping` with `PROPERTY_USAGE_DEFAULT | PROPERTY_USAGE_CHECKABLE`, which carries STORAGE. Their default value is a nil `Variant`, so any bool set on it differs from the default.
- The player strips STORAGE from a parameter whose current value equals the parameter's default (`scene/audio/audio_stream_player_internal.cpp:248-251`), so a fixture must set a value that differs from the default; `true` on `looping` does.
- Parameters reach the player's property list only once a stream is assigned (`scene/audio/audio_stream_player_internal.cpp:238-243`), and the assignment must come before the `set`.
- The plan's guess that the dummy renderer would list no shader uniforms was wrong in 4.7.2: instance parameters and `ShaderMaterial` parameters are both listed and stored headless, because the uniform list is read from the `Shader` resource (its code text), not from a compiled backend.

## Notes for the fixture work

- Families 1-8 are all generated by a headless script run, so each can have a compare fixture; none needs the reviewed exception list.
- `GridMap.make_baked_meshes()` produces an `ArrayMesh` sub-resource with a random id (`ArrayMesh_656ef`, `ArrayMesh_n8k8a` on two runs of one file). The generator pins sub-resource ids with `set_id_for_path` (see `_pin` in `tools/scene-fixtures/generate.gd`), so an un-pinned id would make the fixture unstable across regenerations.
- The instance-shader-parameter families need the `Shader` assigned to the material before `set_instance_shader_parameter`, and the shader code must declare the payload as `instance uniform`.
- No family needed a tree: every "stored" answer is the same with and without the node under the SceneTree root.

RULINGS: the audio parameter's value was left open by the brief; I first set `parameters/switch_to_clip = 0` on `AudioStreamInteractive` per the brief's example, then, after asking, ran the single permitted second attempt with `AudioStreamOggVorbis` and `parameters/looping = true`; the answer from the orchestrator authorised exactly that.

OBSERVATIONS: Godot stored nothing for the interactive-music class with no error printed, which is the trap the second attempt existed to clear; a future probe of an audio class should read its parameter's `PROPERTY_USAGE` first. `ShaderGlobalsOverride` needs `global_shader_parameter_add` before the node's property list holds the name, and the added global then persists in `RenderingServer.global_shader_parameter_get_list()` for the rest of the process.

CALLS: 31
