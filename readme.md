# Proc

1. Wraps `System.Diagnostics.Process` as an `IObservable` 
    * `ProcessObservable` stream based wrapper
    * `EventBasedObservableProcess` event based wrapper
2. Exposes a super handy static `Proc.Start` for the quick oneliners


## Proc.Start

start a process and block using the default timeout of 1 minute
```csharp
var result = Proc.Start("ipconfig", "/all");
```

Provide a custom time out and an `IConsoleOutWriter` that can out put to console 
while this line is blocking. This implementation writes `stderr` in red.
```csharp
var result = Proc.Start("ipconfig", TimeSpan.FromSeconds(10), new ConsoleOutColorWriter());
```

Even more explicit you can pass `StartArguments` to control how the process should start.

```csharp
var args = new StartArguments("ipconfig", "/all")
{
  WorkingDirectory = ..
}
Proc.Start(args, TimeSpan.FromSeconds(10));
```

`result` has the following propeties

* `Completed` true if the program completed before the timeout
* `ConsoleOut` a list the console out message as `LineOut` 
   instances where `Error` on each indicating wheter it was written on `stderr` or not
* `ExitCode` 

**NOTE** `ConsoleOut` will always be set regardless of whether an `IConsoleOutWriter` is provided

## ObservableProcess

The heart of it all this is an `IObservable<CharactersOut>`

