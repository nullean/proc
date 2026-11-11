---
name: proc-fs
description: >-
  Use when F# code (build scripts, tools, tests) needs to run commands: a sequence of commands like a shell script,
  capturing output, piping, or changing directory and environment for a few commands. Proc.Fs gives shell { },
  exec { } and the Cmd module on top of Proc.
---

# Proc.Fs

`open Proc.Fs`. `shell { }` runs commands in order like a shell script; the first failure throws
`ProcExecException` and stops the block. `shellx { }` also prints each command first, like `set -x`.

```fsharp
let sha = shell {
    sh $"git fetch"
    let! sha = text $"git rev-parse HEAD"                  // stdout, trimmed
    let! files = lines $"git diff --name-only | grep \.fs$" // stdout as lines; | pipes
    for file in files do
        sh $"dotnet fantomas {file}"                       // {file} is always exactly one argument
    let! code = exitCode $"git diff --quiet"               // no throw
    let! ok = succeeds $"which docker"                     // exit code 0, no throw
    return sha
}
```

Directory and environment, for the rest of the block or a scope:

```fsharp
shell {
    env "CI" "true"                                  // for the rest of the block, like export
    unsetEnv "NUGET_API_KEY"
    for project in ["src/Proc"; "src/Proc.Fs"] do
        use! _ = inDir project                       // restored after the loop body, even on an exception
        use! _ = withEnv ["Configuration", "Release"]
        sh $"dotnet pack"
}
```

## Gotchas

- `sh` takes an interpolated string and splits only the text you wrote: whitespace separates arguments, quotes group
  them, and an interpolated value never splits. Do not build the command string with `sprintf` first.
- Unquoted `>`, `<`, `;` and `&` throw: there is no redirection or chaining. Quote them to pass them literally, and
  use separate `sh` lines instead of `;` or `&&`.
- A `shell { }` block runs as soon as it is evaluated; wrap it in a function to run it later.
- The package's README has the full list of steps, `exec { }` and `Cmd`.
