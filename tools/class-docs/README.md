# class-docs

Writes `metadata/docs/godot-<major>.<minor>.json`: a Godot version's class reference, from the engine's `doc/classes` and its modules' `doc_classes`, as Markdown for the language server's hover. One file per Godot version, versioned the way `metadata/godot-<major>.<minor>.json` is; the folder keeps it out of the Core project's `metadata/godot-*.json` embed glob, which does not recurse.

## Run

```
pwsh -NoProfile -File tools/class-docs/generate.ps1 -GodotSource <godot source> -Metadata metadata/godot-4.7.json -GodotVersion 4.7.2 -Out metadata/docs/godot-4.7.json
```

- `-GodotSource`: the root of a Godot source tree, e.g. `F:\Godot\src-4.7.2`; its `doc/classes` and every `modules/*/doc_classes` are read, so module classes (`CSGBox3D`, `GridMap`, `AudioStreamMP3`) get an entry too. Two doc files naming the same class is an error that names both files.
- `-Metadata`: the metadata file whose classes get an entry, `metadata/godot-4.7.json` for 4.7.
- `-GodotVersion`: the full version the docs come from, e.g. `4.7.2`; it names the version in the file's header and the manual URL below.
- `-Out`: the JSON file to write.

Only classes the metadata lists are written, plus `@GlobalScope`, whose constants hold the global enums. A metadata class no doc file names is skipped, and the run's last line says how many were. A source with no `doc/classes/@GlobalScope.xml`, or in which no metadata class got written, is an error, and so is a doc file that is not well-formed XML or a metadata file with no `classes` object; each names the file it read. Keys are sorted ordinally at every level, the indent is two spaces, lines end in LF and the file ends with a newline, so two runs on the same source give identical bytes.

## Output

Top level: `format` (1, the format's version), `godot` (the full Godot version), `source` (`Converted from Godot 4.7.2 doc/classes and modules/*/doc_classes`), `license` (Godot's notice, below) and `classes`.

A class entry has `brief` and `description` (Markdown), `members`, `signals`, `constants` and `themeItems` (objects keyed by name, present even when empty), and `deprecated` / `experimental` when the class carries the attribute.

An entry under `members`, `signals`, `constants` or `themeItems` is an object with a Markdown `description` (`""` when the XML gives none), plus `enum` (constants only, when the XML names one), and `deprecated` / `experimental` (Markdown) when the attribute is present — a bare `deprecated=""` or `experimental=""` means "deprecated, no message" and writes `""`. Methods, constructors, operators, annotations and tutorials are left out: hover over `.xscn` and `.xres` never shows them.

The conversion, Godot BBCode to Markdown:

- `[b]x[/b]` is `**x**` and `[i]x[/i]` is `*x*`; whitespace just inside the markers moves outside them, and a run across a line break is closed and reopened on each line. A literal `*` in the text is written `\*`, so only the conversion's own marking is emphasis.
- `[code]x[/code]` and `[kbd]x[/kbd]` are inline code, fenced one backtick longer than any run of backticks inside; a parameterised `[code skip-lint]` is inline code too, and an empty one writes nothing.
- `[url=X]t[/url]` is `[t](X)` with `t` converted like any other text, a bare `[url]X[/url]` is `<X>`, and `$DOCS_URL` is `https://docs.godotengine.org/en/<major>.<minor>` outside code, which keeps it as written.
- A reference is inline code with the Godot name: `[ClassName]` and `[param x]`, and `[member A.b]`, `[method A.b]`, `[signal A.b]`, `[constant A.B]`, `[enum A.B]`, `[theme_item b]`, `[annotation @x]`, `[constructor …]`, `[operator …]`, each as the name written after the tag.
- `[codeblocks]` keeps only its `[csharp]` part, as a `csharp` fenced block, and is dropped when it has none; `[codeblock]` is a fenced block, `[codeblock lang=x]` one fenced as `x`, and both take further arguments (`[codeblock skip-lint]`) without changing that. A fence is longer than any run of backticks in its body. Nothing inside a code block is converted.
- `[lb]` and `[rb]` are `\[` and `\]`, escaped so that they open no Markdown link; `[u]`, `[s]`, `[center]`, `[color]`, `[font]`, `[font_size]`, `[outline_size]`, `[outline_color]`, `[bgcolor]`, `[fgcolor]`, `[hint]`, `[indent]`, `[p]`, `[dropcap]`, `[ul]`, `[ol]`, `[table]` and `[cell]` are dropped with their text kept, and so is a tag left unpaired.
- The XML text is dedented by the tabs its lines share, and written one paragraph per line with a blank line between, as Markdown needs.

## Check

```
pwsh -NoProfile -File tools/class-docs/check.ps1
```

`check.ps1` dot-sources `generate.ps1` and runs the converter over one case per rule above, printing `CHECK <name> PASS` or `CHECK <name> FAIL <detail>` for each, then `DONE <passed>/<total>`, and exits 1 when any case failed.

## License

`doc/classes/*.xml` is Expat, i.e. MIT: Godot's `COPYRIGHT.txt` has no entry for `doc/`, so the default `Files: *` entry covers it. MIT asks that the copyright and permission notice be included in every copy of the work, so the generated file carries it in its `license` field and `metadata/docs/LICENSE-godot.txt` is a verbatim copy of the engine's `LICENSE.txt` beside it. The converted text is a derived work; the `source` field names the version and folder it came from.

## When to run it

On each new Godot minor version, with that version's source tree and the metadata file dumped from the same version, then commit the result. A patch release keeps the class reference, so the file for its minor version stays.
