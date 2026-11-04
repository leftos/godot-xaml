---
name: prepare-release
description: Prepare and publish a godot-xaml release — version, changelog, gates, packages, tag, GitHub release, Linear cut (invoke by hand as /prepare-release).
disable-model-invocation: true
---

Prepare and publish a godot-xaml release. Walk through the phases in order, from the repo root; each phase ends on its **Done when** line, and any red stops the cut until the owner rules.

The release ships the core, the CLI and the addon package at one version, on one track of builds: every release is `--latest`.

**Ground rules for every phase:**

- `linear` is the command `~/.claude/docs/plan-operations.md` defines.
- Every long command runs through `tools/gate.ps1` or `run.ps1`; a status comes from the gate's verdict, read unpiped.
- A commit message is written to `.tmp/msg-<slug>.txt`, a file new to that commit, in an earlier turn than the commit, and files are enumerated in the commit's pathspec.

## Phase 1: Ground

1. Confirm the tree is on `main`, clean and level with `origin` (`git fetch origin`, `git status -sb`).
2. Run `linear release status godot-xaml`. An open issue in `vNext` stops the cut until the owner says otherwise.
3. Ask the owner for the version `<v>`, as `major.minor.patch`. The suggested one comes from what the release holds, as the **release** row of `~/.claude/docs/plan-operations.md` says: the next minor for a new feature, the next patch for fixes alone.

**Done when** the tree is level with `origin`, `vNext` holds no open issue the owner has not waved through, and the owner has named `<v>`.

## Phase 2: Version

Set `<v>` in the seven homes `tools/pack-release.ps1` checks:

- `VersionPrefix` in `Directory.Build.props`;
- `version` in `godot/addons/godot_xaml/plugin.cfg`;
- the `GodotXaml.Core` `PackageReference` in `README.md`;
- every `godot-xaml-addon-<v>.zip` name in `README.md`'s install steps;
- the `GodotXaml.Cli --version <v>` install line in `README.md`;
- the `GodotXaml.Cli --version <v>` install line in `godot/addons/godot_xaml/AGENTS.md`;
- every `godot-xaml/blob/v<v>/` link, and the version line, in `godot/addons/godot_xaml/AGENTS.md`.

Then run `pwsh -NoProfile -File tools/pack-release.ps1 -Version <v> -CheckOnly`, which checks the seven homes, refuses a `v<v>` tag already on `origin` (the minutes before nuget.org lists a pushed version) and a version nuget.org lists, and packs nothing.

**Done when** all seven read `<v>` and `-CheckOnly` prints `pack-release.ps1: <v> is free to release`.

## Phase 3: Changelog

1. Create `CHANGELOG.md` if the repo has none, then add a section `## <v> - <date>`, newest first, written from the Done issues `linear release status` lists in `vNext`.

   Follow "Step 4: Write the bullets" of the `changelog-and-commit` skill: each bullet one sentence of at most 25 words in the product's own words, and the section read as the difference from the last release.
2. Make `README.md` true for this version, in its Status section and its Install steps. At the first cut, remove "Not released yet." and the sentence on the local feed.

**Done when** every Done issue in `vNext` with a user-visible change has its bullet, and each README claim about releases and installing holds at `<v>`.

## Phase 4: Gates

Write `.tmp/release-gates.sh`, a bash script with `set -e` that runs one gate at a time, and start it with `run_in_background`. Each step is named in the log:

1. `pwsh -NoProfile -File run.ps1 test`
2. `pwsh -NoProfile -File run.ps1 godot-build`
3. `pwsh -NoProfile -File run.ps1 format-check`
4. `godot/checks.ps1`, `godot/compare.ps1`, `godot/editor-checks.ps1` and `godot/export.ps1`, each with its gate line from "Test locations" in `docs/ARCHITECTURE.md`; the editor checks open the real editor, so they are the only gate that runs the addon's editor plugin

5. When `git diff <last tag>..HEAD --stat` touches the release path (`.github/workflows/build-server.yml`, `tools/publish-server.ps1`, `tools/sign-macos.ps1`, `tools/smoke-server.ps1`), run `gh workflow run build-server.yml --ref main` once `main` is on `origin` (the dispatch builds the remote branch) and wait for it, both smoke-signed jobs green, before tagging. With no tag it is a dry run that attaches nothing.

To stop the script, use `pwsh -NoProfile -File tools/gate.ps1 stop-tree <its bash pid>`.

**Done when** the script exits 0 with every gate's verdict passed.

## Phase 5: Commit and tag

The packages carry the commit they were built from, so the release commit comes first.

1. Commit `CHANGELOG.md`, `README.md`, `Directory.Build.props`, `godot/addons/godot_xaml/plugin.cfg` and `godot/addons/godot_xaml/AGENTS.md` with the message `chore: release <v>`:

   `pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/commit.log --slot light --timeout 1200 -- git commit -F .tmp/msg-release-<v>.txt -- CHANGELOG.md README.md Directory.Build.props godot/addons/godot_xaml/plugin.cfg godot/addons/godot_xaml/AGENTS.md`
2. Tag it: `git tag -a v<v> -m "godot-xaml <v>"`.

**Done when** `git status --short` is empty and `git rev-parse "v<v>^{commit}"` is `HEAD`.

## Phase 6: Pack

```
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/pack.log --slot heavy --timeout 900 -- pwsh -NoProfile -File tools/pack-release.ps1 -Version <v>
```

**Done when** it exits 0 and lists `GodotXaml.Core.<v>.nupkg`, `GodotXaml.Cli.<v>.nupkg` and `godot-xaml-addon-<v>.zip`, all under `.tmp/release/<v>/`.

To try an install before a release, pack with `-Local` and point the trial project at that folder, naming the exact version the pack printed (`<PackageReference Include="GodotXaml.Core" Version="<v>-local.<UTC stamp>" />`, or `dotnet tool install --prerelease --version <v>-local.<UTC stamp>` for the CLI): a reference to `<v>` restores the published package instead.

A local version never takes a published version's place in the shared NuGet packages folder, which keeps the first package restored under a version and never fetches it again. Delete the `*-local.*` folders under `godotxaml.core` and `godotxaml.cli` in that packages folder once the trial is done. `tools/pack-release.ps1` refuses, without `-Local`, a version whose tag is on `origin` or which nuget.org lists. A `-Local` zip's `plugin.cfg` names the local version, so the trial project's plugin list shows it.

## Phase 7: Push and publish

1. Push the branch, then the tag: `git push origin main`, `git push origin v<v>`.
2. Write the changelog section for `<v>`, without its heading, to `.tmp/release-notes-<v>.md`.
3. Create the GitHub release, attaching all three files:

   `gh release create v<v> --latest --title "godot-xaml <v>" --notes-file .tmp/release-notes-<v>.md .tmp/release/<v>/GodotXaml.Core.<v>.nupkg .tmp/release/<v>/GodotXaml.Cli.<v>.nupkg .tmp/release/<v>/godot-xaml-addon-<v>.zip`
4. The packages reach nuget.org by trusted publishing, with no API key: publishing the release runs `.github/workflows/publish-nuget.yml`. The workflow downloads the release's `.nupkg` files, exchanges its GitHub OIDC token for a one-hour nuget.org key (the nuget.org policy "godot-xaml GitHub releases" on account `leftos` trusts that file, for `GodotXaml.*`), and pushes them with `--skip-duplicate`.

   Watch it with `gh run list --workflow publish-nuget.yml --limit 1`, then `gh run watch <id> --exit-status` through the gate's `--slot none`. When the release event did not start it, run it by hand: `gh workflow run publish-nuget.yml -f tag=v<v>`. That happens when the tag's commit predates the workflow file, since a release event runs the workflow as the tag's commit has it.
5. Publishing the release also runs `.github/workflows/build-server.yml`, which builds the language server's six NativeAOT zips on their native runners, signs and notarises the two macOS ones, and attaches the six `godot-xaml-ls-<v>-<rid>.zip` files to the release with `--clobber`. It takes a while. Watch it with `gh run list --workflow build-server.yml --limit 1`, then `gh run watch <id> --exit-status` through the gate's `--slot none`; the run must include both Sign and notarise jobs and both smoke-signed jobs, which also fail a macOS zip whose `GodotXaml.LanguageServer` is not at its root. When the release event did not start it, dispatch it by hand once the release exists: `gh workflow run build-server.yml -f tag=v<v>`. That happens when the tag's commit predates the workflow file. A failed run is re-run, or dispatched again; it attaches nothing until every leg has passed.
6. Install the agent skill on this machine, its links pinned to the tag just pushed: `pwsh -NoProfile -File tools/install-skill.ps1 -Version <v>`.

**Done when** all of these hold:

- `git ls-remote --tags origin v<v>` shows the tag;
- `gh release view v<v>` lists the three files plus six `godot-xaml-ls-<v>-<rid>.zip` assets, and marks the release as latest;
- the build-server run for `v<v>` passed;
- the publish workflow's run for `v<v>` passed, and `https://api.nuget.org/v3-flatcontainer/godotxaml.core/index.json` lists `<v>`;
- `~/.claude/skills/godot-xaml` is a junction to `%LOCALAPPDATA%\godot-xaml\versions\<v>\skill`.

## Phase 8: Cut

1. Run `linear release complete godot-xaml --version <v> --sha <tag commit> --repo-root .`, where `<tag commit>` is `git rev-parse "v<v>^{commit}"`. It runs once per version; shipped issues it leaves out go through `linear release settle`, as the **release** row of `~/.claude/docs/plan-operations.md` gives.
2. Take the plan **snapshot**: `linear snapshot godot-xaml --repo-root .`, committed on its own as `docs: plan snapshot`, and push it.

**Done when** the completed release `<v>` holds every shipped issue and the snapshot is on `origin/main`.

## Phase 9: Next release

Run "Plan the next release with the owner" from the **release** row of `~/.claude/docs/plan-operations.md`: one `AskUserQuestion` round, applied with `linear release add`.

**Done when** every candidate group is in `vNext`, in `vNext+1` or in no release, as the owner answered.

## Hotfix

A hotfix follows `~/.claude/docs/plan-operations.md`, "Hotfix". The same phases apply from the hotfix branch in place of `main`, with `--hotfix` on the `linear release complete` of Phase 8; Phase 9 follows the plain cut only.
