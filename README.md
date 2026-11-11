<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-mark-128.png">
    <img src="https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-mark-light-128.png" width="96" alt="Proc">
  </picture>
</p>

<h1 align="center">Proc</h1>

<p align="center">A <code>System.Diagnostics.Process</code> supercharger: one-liners that run a program, capture or stream its output, and stop it with Ctrl+C before killing it.</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Proc"><img src="https://img.shields.io/nuget/v/Proc?style=flat-square" alt="NuGet"></a>
  <a href="https://github.com/nullean/proc/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/nullean/proc/ci.yml?branch=main&style=flat-square&label=ci" alt="CI"></a>
</p>

```csharp
var result = Proc.Start("git", "status", "--short");   // streams to the console and captures every line
```

```fsharp
let sha = shell { return! text $"git rev-parse HEAD" }  // each {value} is always one argument
```

## Install

```sh
dotnet add package Proc       # C#
dotnet add package Proc.Fs    # F#: shell { }, exec { } and the Cmd module
```

## Quick start

```csharp
using ProcNet;

// run a program on the shared console; throws ProcExecException on a non-zero exit code
Proc.Exec("dotnet", "--version");

// capture its output, while still writing it to the console as it arrives
var result = Proc.Start("git", "status", "--short");
foreach (var line in result.ConsoleOut)
	Console.WriteLine($"{(line.Error ? "err" : "out")}: {line.Line}");

// start a server, go on once it says it is ready, and stop it with Ctrl+C when disposed
var args = new LongRunningArguments("dotnet", "run", "--project", "api")
{
	StartedConfirmationHandler = l => l.Line.Contains("Now listening"),
	SendControlCFirst = true
};
using (var api = Proc.StartLongRunning(args, TimeSpan.FromMinutes(1)))
{
	Proc.Exec("dotnet", "test", "tests/Api.Tests");
}
```

In F#, a `shell { }` block runs commands in order and stops at the first one that fails. Commands are interpolated
strings, and each `{value}` is always one argument, so values never need quoting:

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

A block can also start a background process and stop it at the end of a scope, run clean-up commands, and run blocks
at the same time:

```fsharp
shell {
    use! api = background (sh $"dotnet run --project api" |> Cmd.waitFor (fun l -> l.Contains "Now listening"))
    use! _ = defer (sh $"docker compose down")
    let! _ = concurrently [ steps { sh $"dotnet test tests/A" }; steps { sh $"dotnet test tests/B" } ]
    return ()
}
```

## What it does

- `Proc.Exec()` for the quick one-liners, `Proc.ExecAsync()` for the same with a `CancellationToken`.
- `Proc.Start()` captures the console output as well as printing it in real time, and lets you script standard input.
- `Proc.StartLongRunning()` starts a server or database and returns once it prints that it is ready.
- `ObservableProcess` and `EventBasedObservableProcess` wrap `System.Diagnostics.Process` as an `IObservable`.
  `ObservableProcess` listens on the output buffers directly, so it sees prompts that never end in a newline.
- `SendControlCFirst = true` sends `SIGINT` before falling back to a hard `SIGKILL` (`Process.Kill()`), so a process
  can shut down cleanly.
- On .NET 11 it uses the new Process APIs: in-process `SIGINT` on Linux and macOS, `KillOnParentExit` and
  `InheritedHandles`.

## Packages

| Package | What it is | |
|---|---|---|
| [`Proc`](src/Proc/README.md) | the C# API: `Proc.Exec`, `Proc.Start`, `Proc.StartLongRunning` and the observable processes | [![NuGet](https://img.shields.io/nuget/v/Proc?style=flat-square)](https://www.nuget.org/packages/Proc) |
| [`Proc.Fs`](src/Proc.Fs/README.md) | F# bindings: `shell { }`, `exec { }`, the `Cmd` module and pipes | [![NuGet](https://img.shields.io/nuget/v/Proc.Fs?style=flat-square)](https://www.nuget.org/packages/Proc.Fs) |

## Learn more

- [Proc reference](src/Proc/README.md): every call and option, `SIGINT` handling, line boundaries and .NET 11
- [Proc.Fs reference](src/Proc.Fs/README.md): `shell { }`, background processes, `sh` quoting rules, `Cmd`, pipes and `exec { }`
- [Releases](https://github.com/nullean/proc/releases)
- [Contributing](https://github.com/nullean/.github/blob/main/CONTRIBUTING.md)

MIT licensed. Copyright Nullean and contributors.
