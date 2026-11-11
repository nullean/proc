# Proc.Fs

F# bindings for Proc. 

Proc is a library that turns `System.Diagnostics.Process` into a stream based `IObservable` capable of capturing a process's true output while still emitting line based events.

The library ships with two computation expression builders, `shell { }` and `exec { }`, and a `Cmd` module for running commands with `|>`.

# Shell

`shell { }` runs commands one after the other, the way a shell script does.
The first command that fails throws a `ProcExecException` and stops the block.
`for`, `while`, `if`, `match`, `try` and `use` all work inside it.

```fsharp
open Proc.Fs

let sha = shell {
    do! cmd "git" ["fetch"]                                  // do! runs a command
    let! sha = cmd "git" ["rev-parse"; "HEAD"] |> Cmd.text   // let! binds what it prints
    for file in cmd "git" ["diff"; "--name-only"] |> Cmd.lines |> Cmd.where (fun f -> f.EndsWith ".fs") do
        do! cmd "dotnet" ["fantomas"; file]
    return sha
}
```

The block runs as soon as it is evaluated and returns the value of its `return`.

These steps change the commands that follow them in the same block:

```fsharp
shell {
    do! cd "src"                  // relative to the current directory; throws if it does not exist
    do! setEnv "CI" "true"
    do! echoCommands true         // print "$ binary arguments" before each command, like set -x
    let! here = pwd
    do! cmd "dotnet" ["build"]
}
```

A command's own `Cmd.cwd` resolves against the shell's directory, and its own `Cmd.env` wins over `setEnv`.

# Cmd

`cmd binary arguments` builds a command without running it. The `Cmd` module adjusts and runs it, and reads well with `|>`.

```fsharp
let build = cmd "dotnet" ["build"] |> Cmd.cwd "src" |> Cmd.env ["CI", "true"] |> Cmd.timeout (TimeSpan.FromMinutes 5.)

Cmd.run build                     // runs on the shared console, throws when it fails
```

Captures describe what to read from a command. Run them with `Cmd.get`, or bind them with `let!` inside `shell { }`.

| Capture | Returns | Throws on a non-zero exit code |
|---|---|---|
| `Cmd.lines` | standard out, one string per line | yes |
| `Cmd.text` | standard out as one string, trimmed | yes |
| `Cmd.exitCode` | the exit code; output still goes to the console | no |
| `Cmd.result` | every line plus the exit code, as a `ProcessCaptureResult` | no |

Standard error of a captured command goes to the console.
`Cmd.where`, `Cmd.map` and `Cmd.choose` work on the lines of a capture, and `Cmd.mapCapture` on any capture:

```fsharp
let projects =
    cmd "git" ["ls-files"]
    |> Cmd.lines
    |> Cmd.where (fun f -> f.EndsWith "proj")
    |> Cmd.get
```

`Cmd.iter` calls a function for each line while the command runs, without collecting the output first:

```fsharp
cmd "dotnet" ["test"] |> Cmd.iter (fun line -> if line.Contains "Failed" then printfn "%s" line)
```

`Cmd.validExitCode` decides which exit codes count as success; the default is only `0`.

## Pipes

`|>|` streams the standard out of one command into the standard input of the next, like `|` in a shell:

```fsharp
let fsFiles = cmd "git" ["ls-files"] |>| cmd "grep" ["\\.fs$"] |>| cmd "sort" [] |> Cmd.lines |> Cmd.get
```

- `|>|` has the same precedence as `|>`, so you can mix them without parentheses.
- Every command in the pipe must succeed. The exception names the command that failed.
- When a later command exits early, Proc stops the earlier ones; that does not count as a failure.
- Standard error of the earlier commands goes to the console.
- Pipes carry lines of text, not raw bytes.

`Cmd.stdin` writes lines of your own to a command's standard input:

```fsharp
let sorted = cmd "sort" [] |> Cmd.stdin ["b"; "a"] |> Cmd.lines |> Cmd.get
```

A command without `Cmd.stdin` or a pipe gets its standard input closed straight away.

# Exec

A `CE` to make it **REAL** easy to execute processes.

```fsharp
//executes dotnet --help
exec { run "dotnet" "--help" }

//supports lists as args too
exec { run "dotnet" ["--help"] }
```

If you want more info about the invocation
you can use either `exit_code_of` or `output_of`
for the quick one liners.


```fsharp
let exitCode = exec { exit_code_of "dotnet" "--help" }
let output = exec { output_of "dotnet" "--help" }
```
`output` will hold both the exit code and the console output.
`exec { }` blocks build the same `Cmd` value as `cmd`, so `options` accepts one. Run piped commands with `Cmd` or `shell { }`.
`exit_code_of` returns the exit code whatever it is; only `run` and friends throw on a non-zero exit code.

If you need more control on how the process is started 
you can supply the following options.


```fsharp
exec {
    binary "dotnet"
    arguments "--help"
    env Map[("key", "value")]
    working_dir "."
    send_control_c false
    timeout (TimeSpan.FromSeconds(10))
    kill_on_parent_exit true
    filter_output (fun l -> l.Line.Contains "clean")
    valid_exit_code (fun i -> i <> 0)
    run
}
```

- `timeout`: kill the process if it runs longer. Without it the process may run indefinitely.
- `valid_exit_code`: which exit codes count as success (default: only `0`). `validExitCode` still works but is obsolete.
- `kill_on_parent_exit`: terminate the process when yours exits. Only takes effect on .NET 11+ on Windows and Linux.
- `thread_wrap` is obsolete and has no effect.

Processes whose output is captured (`output`, `output_of`, `filter`, `find`) get their standard input closed
straight away, so tools that read stdin, such as `grep` or `sort`, finish instead of waiting for input.

`run` will kick off the invocation of the process.

However there are other ways to kick this off too.

```fsharp
exec {
    binary "dotnet"
    run_args ["restore"; "--help"]
}
```
Shortcut to supply arguments AND run

```fsharp
let linesContainingClean = exec {
    binary "dotnet"
    arguments "--help"
    filter (fun l -> l.Line.Contains "clean")
}
```

run the process returning only the console out matching the `filter` if you want to actually filter what gets written to the console use `filter_output`.


```fsharp

let dotnetHelpExitCode = exec {
    binary "dotnet"
    arguments "--help"
    exit_code
}
```

returns just the exit code, without throwing when it is non-zero (unless `valid_exit_code` is given)

```fsharp

let helpOutput = exec {
    binary "dotnet"
    arguments "--help"
    output
}
```

returns the exit code and the full console output.

```fsharp

let process = exec {
    binary "dotnet"
    arguments "--help"
    wait_until (fun l -> l.Line.Contains "clean")
}
```

returns an already running process but only after it confirms a line was printed
```fsharp

let process = exec {
    binary "dotnet"
    arguments "--help"
    wait_until_and_disconnect (fun l -> l.Line.Contains "clean")
}
```

returns an already running process but only after it confirms a line was printed. This version will stop the yielding standard/out lines which may utilize memory consumption which is no longer needed.
