# Proc

A dependency-free wrapper around `System.Diagnostics.Process`: `Proc.Exec` for one-liners, `Proc.Start` to capture
and react to output, observable process streams, and graceful `SIGINT`-before-kill. `Proc.Fs` adds an F# DSL on top.

## Layout

| Path | What |
|---|---|
| `src/Proc` | the library (netstandard2.0 … net11.0, plus net461) |
| `src/Proc.Fs` | the F# DSL (`shell { }`, `exec { }`), net10.0 and net11.0 |
| `src/Proc.ControlC` | a tiny net40 helper exe that delivers Ctrl+C on Windows; embedded, never packed |
| `tests/Proc.Tests`, `tests/Proc.Fs.Tests` | tests, on net10.0 and net11.0 |
| `tests/Proc.Tests.Binary` | a NativeAOT test binary the tests start, so process-lifecycle tests are fast and stable |
| `build/scripts` | the F# build the `mise` tasks call |

## Commands

mise installs the SDK pinned in `global.json` (plus the net10.0 runtime the tests also run on). Run everything through
it:

```bash
mise run build    # build the solution
mise run test     # build, then run the tests
mise run pack     # build, pack, validate the packages, report public API changes
mise run ci       # exactly what CI runs
mise run format   # format the repository with curb
mise run check    # formatting drift (curb) and workflow security checks (zizmor); CI runs the curb half
mise run release  # show the release draft; `mise run release publish` releases
```

`mise tasks ls` lists them all.

## Releasing

release-drafter keeps a draft release up to date from the labels of merged pull requests. **Publishing the draft**
releases: it creates the tag, and the `release` job in `ci.yml` packs at that version and pushes to nuget.org. `mise run release` shows
the draft. The [releasing](.claude/skills/releasing/SKILL.md) and [breaking-changes](.claude/skills/breaking-changes/SKILL.md)
skills have the details. Every pull request carries exactly one release label. Package output and API reports go to `build/output`.

## Rules

- Public API is a contract: `mise run pack` diffs it against the previous release (`breaking-changes-*.md`). A change
  that removes or alters public API is a breaking change.
- `Proc` must stay dependency-free beyond `System.Reactive`. Do not add package references to `src/Proc`.
- Process-lifecycle tests start `Proc.Tests.Binary`; do not replace it with shell commands, which differ per OS.
