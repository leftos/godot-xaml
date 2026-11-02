# value-fixtures

Writes `tests/fixtures/values/`: the text Godot 4.7 itself gives values in its text grammar (the right-hand side of `name = value` in `.tscn` and `.tres`). The core's value reader and writer are tested against these files, so the expected bytes come from Godot, never from hand.

## Run

```
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/value-fixtures.log --timeout 300 --slot light -- pwsh -NoProfile -File tools/value-fixtures/generate.ps1 -Godot F:/Godot/Godot_console.exe
```

`generate.ps1` takes `-Godot` (else `$env:GODOT_BIN`) and an optional `-OutDir` (default `tests/fixtures/values/`), runs `generate.gd` headless over this folder's project, prints `value-fixtures: wrote N files` and exits with Godot's status. The run fails on a double-precision build (`real_t` must be `float`). A second run writes the same bytes; commit the folder after a run.

## Files

- `cases.gd`: the canonical cases, each `[name, value]` with the value built by Godot.
- `inputs.txt`: loose and legacy input texts, one `name<TAB>text` per line; `<LF>` in a text stands for a newline. A case named `null_*` may read as null; a case named `expects_error_*` must fail to parse.
- `generate.gd`: the generator (extends `SceneTree`, works in `_initialize`).

## Output

Every file is LF with no final newline; each run first deletes the files in the four folders.

- `canonical/<case>.txt`: `var_to_str(value)`.
- `loose/<case>.in.txt` and `loose/<case>.out.txt`: the input text and `var_to_str(str_to_var(text))`; an `expects_error_*` case gets the `.in.txt` only. The generator fails when `str_to_var` returns null for any other case not named `null_*`. An error Godot raises after it has built the value (`Signal(1)`) still leaves `str_to_var` a value, so such inputs are pinned by unit tests instead.
- `floats/doubles.txt`: `<16 hex digits of the double's bits>\t<var_to_str(double)>` per line.
- `floats/float32.txt`: `<8 hex digits of the float's bits>\t<text>`, the text being the first component of `var_to_str(Vector2(x, 0))`.

- `format4/<case>.txt`: the text the resource saver writes with compat off (base64 bytes, `-inf`). The generator saves a `.tres` whose metadata holds the value beside a `PackedVector4Array`, which switches Godot's saver to format 4, and takes the text after `metadata/v = `. It fails when the saved file is not format 4. The cases come from `Cases.format4()` in `cases.gd`.

The float corpora hold edge classes (zero, -0, subnormals, the largest finite values, infinities, NaN, powers of ten 1e-8 to 1e22 and their neighbours, 2^53 and 2^24 neighbours) and 5,000 patterns each from a fixed-seed xorshift64, half of them unrestricted bit patterns and half with exponents near 1e-6 to 1e16.
