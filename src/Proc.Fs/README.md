# Proc.Fs

F# bindings for Proc. 

Proc is a library that turns `System.Diagnostics.Process` into a stream based `IObservable` capable of capturing a process's true output while still emitting line based events.

The library ships with two computation expression builders, `shell { }` and `exec { }`, and a `Cmd` module for running commands with `|>`. It targets .NET 10 and .NET 11.

# Shell

`shell { }` runs commands one after the other, the way a shell script does.
The first command that fails throws a `ProcExecException` and stops the block.
`for`, `while`, `if`, `match`, `try` and `use` all work inside it.

```fsharp
open Proc.Fs

let sha = shell {
    sh $"git fetch"                                       // a command on its own line runs
    let! sha = text $"git rev-parse HEAD"                 // let! binds what it prints
    let! files = lines $"git diff --name-only | grep \.fs$"
    for file in files do
        sh $"dotnet fantomas {file}"                      // {file} is always one argument
    return sha
}
```

The block runs as soon as it is evaluated and returns the value of its `return`.
`shellx { }` does the same, and prints `$ binary arguments` before each command, like `set -x`.

| Inside `shell { }` | Does |
|---|---|
| `sh $"…"` | runs the command; a failure stops the block |
| `let! s = text $"…"` | standard out as one trimmed string |
| `let! ls = lines $"…"` | standard out as a list of lines |
| `let! code = exitCode $"…"` | the exit code, without throwing |
| `let! ok = succeeds $"…"` | whether the exit code is `0`, without throwing |

These steps change the commands that follow them in the same block:

```fsharp
shell {
    cd "src"                      // relative to the current directory; throws if it does not exist
    pushd "tests"                 // save the current directory, then change
    popd                          // back to the saved one; throws when nothing was saved
    env "CI" "true"               // like export CI=true; setEnv is the same
    setEnvs ["A", "1"; "B", "2"]
    unsetEnv "NUGET_API_KEY"      // also removes variables this process has
    echoCommands true             // print "$ binary arguments" before each command, like set -x
    let! here = pwd
    let! home = getEnv "HOME"     // string option: the block's value, else this process's
    sh $"dotnet build"
}
```

`inDir` and `withEnv` last until the end of the enclosing scope, such as a `for` body or a `try`.
The previous directory and values come back afterwards, also after a `cd` or an exception:

```fsharp
shell {
    for project in ["src/Proc"; "src/Proc.Fs"] do
        use! _ = inDir project
        use! _ = withEnv ["Configuration", "Release"]
        sh $"dotnet pack"
}
```

A command's own `Cmd.cwd` resolves against the shell's directory, and its own `Cmd.env` wins over `setEnv`.
`do!` still works in front of commands and steps, for example `do! cd "src"`.

## Writing commands with `sh`

`sh` takes an interpolated string, `$"…"`. It splits **only the text you wrote**:

- Whitespace, including new lines, separates arguments.
- `'…'` and `"…"` group words into one argument. Inside `"…"`, `\"` and `\\` are escapes.
- An unquoted `|` pipes into the next command.
- A `\` at the end of a line continues the command, so commands copied from bash scripts work.
- Unquoted `>`, `<`, `;` and `&` throw. Quote them to pass them as arguments.

Each `{value}` is always part of exactly one argument, whatever it contains: spaces, quotes, `|` or `$(…)`.
A list standing alone, such as `{files}`, adds one argument per item. Formats such as `{n:F2}` use the invariant culture.
No shell runs, so a value can never start another command, and there is no glob or `$VAR` expansion.

| You write | The program gets |
|---|---|
| `sh $"wc -l {file}"` with `file = "my file.fs"` | `wc` with `-l`, `my file.fs` |
| `sh $"git commit -m {msg}"` with `msg = "fix; rm -rf ~"` | `git` with `commit`, `-m`, `fix; rm -rf ~` |
| `sh $"dotnet test {projects}"` with a list | `dotnet` with `test`, then one argument per project |
| `sh $"grep 'a\|b' {f}"` | one command: `grep` with `a\|b` and the file |

Triple-quoted strings span lines and contain `"` without escaping:

```fsharp
sh $"""
    docker run --rm
      -e "CI=true"
      -v {dir}:/src
      alpine ls
"""
```

Use `$$"""…"""` when the command contains literal braces; its placeholders are `{{value}}`.
A plain string without `$` does not compile. `Cmd.ofString` parses text that only exists at runtime.

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
