# Proc.Fs

![Proc](https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-mark-128.png)

F# bindings for Proc: run commands the way a shell script does, with every interpolated value passed as exactly one
argument, so values never need quoting.

[![NuGet](https://img.shields.io/nuget/v/Proc.Fs?style=flat-square)](https://www.nuget.org/packages/Proc.Fs)

[Proc](https://www.nuget.org/packages/Proc) turns `System.Diagnostics.Process` into a stream based `IObservable`
capable of capturing a process's true output while still emitting line based events. This package ships two
computation expression builders, `shell { }` and `exec { }`, and a `Cmd` module for running commands with `|>`.
It targets .NET 10 and .NET 11.

## Install

```sh
dotnet add package Proc.Fs
```

## Quick start

```fsharp
open Proc.Fs

let sha = shell {
    sh $"dotnet build"
    let! sha = text $"git rev-parse HEAD"
    for file in lines $"git ls-files | grep \.fs$" do
        sh $"dotnet fantomas {file}"
    return sha
}
```

The first command that fails throws a `ProcExecException` and stops the block.

## Shell

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
    require "dotnet"              // stops the block when dotnet is not on the PATH
    let! docker = which "docker"  // string option: the full path, using the block's PATH
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

### Background processes, clean-up and running steps at the same time

```fsharp
shell {
    use! api = background (sh $"dotnet run --project api" |> Cmd.waitFor (fun l -> l.Contains "Now listening"))
    use! _ = defer (sh $"docker compose down")
    sh $"docker compose up -d db"
    let! results = concurrently [
        steps { cd "tests/Unit"; sh $"dotnet test" }
        steps { cd "tests/Integration"; sh $"dotnet test" }
    ]
    return results
}   // the api gets Ctrl+C, then compose down runs
```

| Inside `shell { }` | Does |
|---|---|
| `use! p = background cmd` | starts `cmd` and goes on; stops it at the end of the scope |
| `Cmd.waitFor (fun line -> …)` | `background` first waits for a line that says the process is ready |
| `do! wait p` | waits until the background process exits; throws when its exit code is not accepted |
| `use! _ = defer cmd` | runs `cmd` at the end of the scope, also when the scope fails, like `trap … EXIT` |
| `let! rs = concurrently [ … ]` | runs `steps { }` blocks at the same time; results in the same order |
| `let! rs = concurrentlyAtMost n [ … ]` | the same, at most `n` at a time |

`background`:

- The process uses the block's `cd` and `env`. Its output goes to the console.
- Without `Cmd.waitFor`, the block goes on straight away. With it, `background` throws when the process exits before
  it prints the line, or when the line does not come within `Cmd.timeout` (default 60 seconds).
  On a background command, `Cmd.timeout` only limits that wait, as with `wait_until` in `exec { }`.
- When the process exits with an exit code that `Cmd.validExitCode` does not accept (by default anything but `0`),
  the **next command** of the block throws, like `set -e`. An exit code of `0` does not fail the block.
- Stopping sends Ctrl+C first and kills the process when it has not exited after 10 seconds. Stopping never throws.
- The block stops background processes that are still running when it ends, also ones bound with `let!`.
- `p.ProcessId`, `p.HasExited`, `p.ExitCode`, `p.Stop()` and `p.SendControlC()` work on the handle.

`defer` runs its command in the directory and environment of the moment `defer` runs, so a later `cd` does not
change it. When the scope fails and the deferred command fails too, the scope's exception propagates and the
failure of the deferred command goes to standard error.

`steps { }` is a `shell { }` block that does not run when it is evaluated. It runs where you use it, with `do!`,
`let!` or on a line of its own, in the directory and environment of the block that uses it.

`concurrently`:

- Each branch starts with a copy of the directory and environment, so a `cd` in one branch does not reach the
  others or the block.
- The first branch that fails stops the others, and its exception is the one that propagates.
- A background process the block started that fails also fails the branches.
- Output of the branches interleaves line by line on the console.

### Writing commands with `sh`

`sh` takes an interpolated string, `$"…"`. It splits **only the text you wrote**:

- Whitespace, including new lines, separates arguments.
- `'…'` and `"…"` group words into one argument. Inside `"…"`, `\"` and `\\` are escapes.
- An unquoted `|` pipes into the next command.
- A `\` at the end of a line continues the command, so commands copied from bash scripts work.
- `< file` reads standard input from a file, on the first command of a pipe.
  `> file` writes standard out to a file and `>> file` adds to one, on the last command.
  A relative file name resolves against the working directory of the command. `2>` is not supported.
- Unquoted `;` and `&` throw. Quote them, or `<` and `>`, to pass them as arguments.

Each `{value}` is always part of exactly one argument, whatever it contains: spaces, quotes, `|` or `$(…)`.
A list standing alone, such as `{files}`, adds one argument per item. Formats such as `{n:F2}` use the invariant culture.
No shell runs, so a value can never start another command, and there is no glob or `$VAR` expansion.

| You write | The program gets |
|---|---|
| `sh $"wc -l {file}"` with `file = "my file.fs"` | `wc` with `-l`, `my file.fs` |
| `sh $"git commit -m {msg}"` with `msg = "fix; rm -rf ~"` | `git` with `commit`, `-m`, `fix; rm -rf ~` |
| `sh $"dotnet test {projects}"` with a list | `dotnet` with `test`, then one argument per project |
| `sh $"grep 'a\|b' {f}"` | one command: `grep` with `a\|b` and the file |
| `sh $"sort < {input} > {output}"` | `sort` reading `input` and writing `output`, whatever their names contain |

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

## Cmd

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

### Files, failures and retries

| Function | Does | Like |
|---|---|---|
| `Cmd.toFile path` | writes standard out to the file, replacing it | `> file` |
| `Cmd.appendToFile path` | adds standard out to the end of the file | `>> file` |
| `Cmd.stdinFile path` | reads standard input from the file | `< file` |
| `Cmd.allowFailure` | accepts any exit code, also from commands piped into it. A timeout still throws. | `\|\| true` |
| `Cmd.quiet` | hides all output; when the command fails, prints it all before throwing | |
| `Cmd.retry n` | runs a failing command up to `n` more times; a pipe runs again as a whole | |
| `Cmd.retryDelay t` | waits `t` before each new attempt | |
| `Cmd.which name` | the full path of a program on the PATH, or `None` | `which` |

```fsharp
shell {
    require "docker"
    sh $"docker pull {image}" |> Cmd.retry 3 |> Cmd.retryDelay (TimeSpan.FromSeconds 5.)
    sh $"dotnet restore" |> Cmd.quiet
    sh $"git log --oneline > {changelog}"
    sh $"docker rm -f {name}" |> Cmd.allowFailure
}
```

Relative file names resolve against the working directory of the command, so inside `shell { }` against its `cd`.
Standard error stays on the console. A capture such as `Cmd.lines` reads nothing from a command whose standard out goes to a file.
`Cmd.iter` passes lines on again when a retried command runs again.

### Pipes

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

## Exec

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
`exec { }` blocks build the same `Cmd` value as `cmd`, so `options` accepts one, including a pipe.
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
    timeout (TimeSpan.FromSeconds 10.)
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

`run` also supports these, like the `Cmd` functions of the same name. The other keywords throw when they are set.

```fsharp
exec {
    binary "dotnet"
    arguments "restore"
    retry 2                                   // Cmd.retry
    retry_delay (TimeSpan.FromSeconds 5.)     // Cmd.retryDelay
    quiet                                     // Cmd.quiet
    allow_failure                             // Cmd.allowFailure
    stdin_file "input.txt"                    // Cmd.stdinFile; stdin ["a"; "b"] is Cmd.stdin
    to_file "restore.log"                     // Cmd.toFile; append_to_file is Cmd.appendToFile
    run
}
```

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

let running = exec {
    binary "dotnet"
    arguments "--help"
    wait_until (fun l -> l.Line.Contains "clean")
}
```

returns an already running process but only after it confirms a line was printed
```fsharp

let running = exec {
    binary "dotnet"
    arguments "--help"
    wait_until_and_disconnect (fun l -> l.Line.Contains "clean")
}
```

returns an already running process but only after it confirms a line was printed. This version will stop the yielding standard/out lines which may utilize memory consumption which is no longer needed.

## Learn more

- The C# API underneath: [Proc](https://www.nuget.org/packages/Proc)
- Source and issues: https://github.com/nullean/proc
- [Releases](https://github.com/nullean/proc/releases)
