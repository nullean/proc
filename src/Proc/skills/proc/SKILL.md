---
name: proc
description: >-
  Use when C# code needs to run an external process: run a command and check it succeeded, capture its output,
  stream output as it arrives, script stdin, or stop it gracefully. Proc (namespace ProcNet) wraps
  System.Diagnostics.Process with one-liners and an observable stream.
---

# Proc

`using ProcNet;` Pick the call by what you need back:

| Need | Call | Returns |
|---|---|---|
| run it, fail on a non-zero exit | `Proc.Exec("dotnet", "build")` | exit code; throws `ProcExecException` otherwise |
| the same, async | `await Proc.ExecAsync(new ExecArguments("dotnet", "build"), ct)` | exit code |
| its output | `Proc.Start("git", "status", "--short")` | `ProcessCaptureResult`: `Completed`, `ExitCode`, `ConsoleOut` |
| each line as it arrives | `Proc.StartRedirected(handler, "dotnet", "test")` | `ProcessResult` |
| a long-running server you wait on | `Proc.StartLongRunning(args, waitForStartedConfirmation)` | a subscription; dispose it to stop |

```csharp
// options go on ExecArguments / StartArguments
Proc.Exec(new ExecArguments("npm", "ci")
{
    WorkingDirectory = "web",
    Environment = new Dictionary<string, string> { ["CI"] = "true" }, // a null value removes a variable
    Timeout = TimeSpan.FromMinutes(5),                                  // kills the whole process tree, then throws
    ValidExitCodeClassifier = code => code is 0 or 2,
});

var result = Proc.Start(new StartArguments("git", "rev-parse", "HEAD") { Timeout = TimeSpan.FromSeconds(30) });
var sha = result.ConsoleOut.First(l => !l.Error).Line;
```

## Gotchas

- Pass every argument separately: `Proc.Exec("git", "commit", "-m", message)`, never one string with spaces.
  Each one reaches the process unchanged, spaces included.
- There is **no default timeout**: without `Timeout` a call waits for the process to exit.
- `Proc.Start` does not throw on a non-zero exit; check `result.ExitCode` (it is `null` when the process did not
  complete). `Proc.Exec` does throw.
- `ConsoleOut` holds stdout and stderr interleaved; `LineOut.Error` tells them apart.
- `SendControlCFirst = true` on `StartArguments` sends SIGINT (Ctrl+C on Windows) before killing, so the process can
  shut down cleanly.
- `KillOnParentExit = true` stops the child when this process dies, on .NET 11+ on Windows and Linux only.

F# code should use `Proc.Fs` (`shell { }`) instead.
