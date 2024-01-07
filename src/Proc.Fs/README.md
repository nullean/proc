# Proc.Fs

F# bindings for Proc. 

Proc is a library that turns `System.Diagnostics.Process` into a stream based `IObservable` capable of capturing a process's true output while still emitting line based events.

The library ships with two computation expression builders:

# Shell 

Under development but this allows you to easily chain several process invocations where execution will stop if any process yields an exit_code other than `0`

```fsharp
let _ = shell {
    exec "dotnet" "--version"
    exec "uname"
}
```

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
`output` will hold both the exit code and the console output

If you need more control on how the process is started 
you can supply the following options.


```fsharp
exec {
    binary "dotnet"
    arguments "--help"
    env Map[("key", "value")]
    workingDirectory "."
    send_control_c false
    timeout (TimeSpan.FromSeconds(10))
    thread_wrap false
    filter_output (fun l -> l.Line.Contains "clean")
    validExitCode (fun i -> i <> 0)
    run
}
```

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

returns just the exit code

```fsharp

let helpOutput = exec {
    binary "dotnet"
    arguments "--help"
    output
}
```

returns the exit code and the full console output.
