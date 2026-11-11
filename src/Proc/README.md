# Proc

![Proc](https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-mark-128.png)

A `System.Diagnostics.Process` supercharger: one-liners that run a program, capture or stream its output, and stop it
with Ctrl+C before killing it.

[![NuGet](https://img.shields.io/nuget/v/Proc?style=flat-square)](https://www.nuget.org/packages/Proc)

## Install

```sh
dotnet add package Proc
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
Console.WriteLine($"exit code {result.ExitCode}");
```

Proc does four things:

1. `Proc.Exec()` for the quick one-liners.
2. `Proc.Start()` when you want to capture the console output as well as print it in real time.
   It also lets you script standard input and react to messages.
3. It wraps `System.Diagnostics.Process` as an `IObservable`:
   - `ObservableProcess`, a stream based wrapper;
   - `EventBasedObservableProcess`, an event based wrapper.
4. It can send `SIGINT` to a process before falling back to a hard `SIGKILL` (`Process.Kill()`).
   Set `SendControlCFirst = true` on `StartArguments` to opt in.

Pick the call by what you need back:

| Need | Call | Returns |
|---|---|---|
| run it, fail on a non-zero exit | `Proc.Exec("dotnet", "build")` | exit code; throws `ProcExecException` otherwise |
| the same, async | `await Proc.ExecAsync(new ExecArguments("dotnet", "build"), ct)` | exit code |
| its output | `Proc.Start("git", "status", "--short")` | `ProcessCaptureResult`: `Completed`, `ExitCode`, `ConsoleOut` |
| each line as it arrives | `Proc.StartRedirected(handler, "dotnet", "test")` | `ProcessResult` |
| a long-running server you wait on | `Proc.StartLongRunning(args, waitForStartedConfirmation)` | a subscription with `ProcessId` and `ExitCode`; dispose it to stop |

Pass every argument separately, `Proc.Exec("git", "commit", "-m", message)`, never one string with spaces: each one
reaches the process unchanged. There is no default timeout: without `Timeout` a call waits for the process to exit.

## Proc.Exec

Executes a process and blocks until it exits. It uses the same console session as your program, so the program's
output goes straight to the console. Throws a `ProcExecException` when the program fails to start, exits with an exit
code that `ValidExitCodeClassifier` does not accept (by default anything but `0`), or runs past its `Timeout`.
See `ExecArguments` for more options.

```csharp
Proc.Exec("ipconfig", "/all");
```

```csharp
Proc.Exec(new ExecArguments("npm", "ci")
{
	WorkingDirectory = "web",
	Environment = new Dictionary<string, string> { ["CI"] = "true" }, // a null value removes a variable
	Timeout = TimeSpan.FromMinutes(5),                                  // kills the whole process tree, then throws
	ValidExitCodeClassifier = code => code is 0 or 2
});
```

## Proc.Start

Starts a process and blocks until it exits, capturing its output.

```csharp
var result = Proc.Start("ipconfig", "/all");
```

Pass `StartArguments` to control how the process starts, for instance with a timeout and an `IConsoleOutWriter` that
writes to the console while the call blocks. `ConsoleOutColorWriter`, the default, writes `stderr` in red.

```csharp
var result = Proc.Start(new StartArguments("ipconfig", "/all")
{
	WorkingDirectory = "..",
	Timeout = TimeSpan.FromSeconds(10),
	ConsoleOutWriter = new ConsoleOutColorWriter()
});
```

`Proc.Start` does not throw on a non-zero exit code. `result` has these properties:

- `Completed`: true if the program completed before the timeout.
- `ConsoleOut`: the console output as a list of `LineOut`, where `Error` says whether a line was written on `stderr`.
- `ExitCode`.

`ConsoleOut` is always set, whether or not an `IConsoleOutWriter` is provided.

## Proc.StartLongRunning

Start a process that keeps running, such as a web server or a database, and go on once it says it is ready.
Disposing the returned subscription stops the process.

```csharp
var args = new LongRunningArguments("dotnet", "run", "--project", "api")
{
	StartedConfirmationHandler = l => l.Line.Contains("Now listening"),
	SendControlCFirst = true
};
using (var api = Proc.StartLongRunning(args, TimeSpan.FromMinutes(1)))
{
	// api.ProcessId, api.ExitCode (once it exited on its own), api.WaitForCompletion(timeout)
}
```

`StartLongRunning` waits for a line that `StartedConfirmationHandler` accepts. Without a handler it returns straight away.
When no such line comes in time, it stops the process and throws `ProcExecException`.
`ExitCode` stays `null` while the process runs, and after disposing stopped it.

## ObservableProcess

The heart of it all is an `IObservable<CharactersOut>`. It listens on the output buffers directly and does not wait on
newlines to emit.

To create an observable process manually, follow this pattern:

```csharp
var args = new StartArguments("dotnet", "--info");
using (var p = new ObservableProcess(args))
{
	p.Subscribe(c => Console.Write(c.Characters));
	p.WaitForCompletion(TimeSpan.FromSeconds(2));
}
```

The observable is cold until subscribed, and is not meant to be reused or subscribed to more than once. To share a
subscription, look into Rx's `Publish`.

The `WaitForCompletion()` call blocks so that `p` is not disposed, which would shut down the started process.

By default a shutdown goes through `Process.Kill`, a hard `SIGKILL` on the process. Proc also supports `SIGINT`, so a
process can shut down cleanly:

```csharp
var args = new StartArguments("elasticsearch.bat")
{
	SendControlCFirst = true
};
```

This sends a Ctrl+C into the running process's console first, then falls back to `Process.Kill`. On Windows Proc
delivers it through a small helper program it ships. On Linux and macOS it shells out to `kill -SIGINT`, unless running
on .NET 11+ (see below), where it delivers the signal in-process instead.

Dealing with `char[]` buffers might not be what you want to program against, so `ObservableProcess` also emits lines:

```csharp
using (var p = new ObservableProcess(args))
{
	p.SubscribeLines(c => Console.WriteLine(c.Line));
	p.WaitForCompletion(TimeSpan.FromSeconds(2));
}
```

Instead of passing on the buffers as they are received, this buffers and only emits whole lines.

In some cases it is useful to introduce your own line boundaries:

```csharp
public class MyProcObservable : ObservableProcess
{
	public MyProcObservable(string binary, params string[] arguments) : base(binary, arguments) { }

	public MyProcObservable(StartArguments startArguments) : base(startArguments) { }

	protected override bool BufferBoundary(char[] stdOut, char[] stdErr)
	{
		return base.BufferBoundary(stdOut, stdErr);
	}
}
```

Returning true from `BufferBoundary` yields the buffered text to `SubscribeLines()`. This is useful when your process
prompts without a new line:

> Continue [Y/N]: <no newline here>

A more concrete example: a `bat` file on Windows that gets a `SIGINT` *always* prompts

> Terminate batch job (Y/N)?

which would not yield to `SubscribeLines` and would block wait handles unnecessarily. `ObservableProcess` handles this
case out of the box and replies with `Y` on `stdin`.

`ObservableProcess` also yields whatever is left in the buffer before `OnCompleted()`.

## EventBasedObservableProcess

The sibling of `ObservableProcess` that uses `OutputDataReceived` and `ErrorDataReceived`, and so can only emit lines.

On .NET 11+ it reads through [`Process.ReadAllLinesAsync`](https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/)
instead, which multiplexes `stdout` and `stderr` on a single thread without blocking any thread pool threads.

## .NET 11

Proc targets `net11.0` alongside its other target frameworks, and picks up a few of the
[Process API improvements shipped in .NET 11](https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/)
when running on it. They are opt-in or no-ops elsewhere, so there is nothing to change if you stay on an older target
framework.

- `SendControlC` and `SendControlCFirst` deliver `SIGINT` on Linux and macOS through `SafeProcessHandle.Signal`
  instead of shelling out to the `kill` binary.
- `EventBasedObservableProcess` reads through `Process.ReadAllLinesAsync` (see above).
- `Proc.Exec` without a `Timeout` calls `Process.Run` directly, and `Proc.ExecAsync` without a `Timeout` and with a
  token that cannot be cancelled calls `Process.RunAsync` directly. With a timeout or a cancellable token both keep
  using Proc's own wait loop: on timeout or cancellation `Process.Run` and `RunAsync` kill only the started process,
  whereas Proc kills its entire process tree and throws.
- Two new options on `StartArguments` and `ExecArguments`, both no-ops on older target frameworks or unsupported
  platforms:

```csharp
var args = new StartArguments("some-long-running-tool")
{
	// Kills the started process if this process exits, including crashes. Backed by Job objects on Windows
	// and PR_SET_PDEATHSIG on Linux. Only takes effect on .NET 11+ on Windows or Linux.
	KillOnParentExit = true,

	// Restricts which handles the started process inherits, instead of every inheritable handle from this
	// process. Only takes effect on .NET 11+.
	InheritedHandles = new List<SafeHandle>()
};
```

## Learn more

- F# bindings: [Proc.Fs](https://www.nuget.org/packages/Proc.Fs), with `shell { }`, `exec { }` and the `Cmd` module
- Source and issues: https://github.com/nullean/proc
- [Releases](https://github.com/nullean/proc/releases)
