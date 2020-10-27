# Proc

<a href="https://www.nuget.org/packages/Proc/"><img src="https://img.shields.io/nuget/v/Proc?color=blue&style=plastic" /></a>

<img src="https://github.com/nullean/proc/raw/master/build/nuget-icon.png" align="right"
     title="Logo " width="220" height="220">

A dependency free `System.Diagnostics.Process` supercharger. 

1. `Proc.Exec()` for the quick one-liners
2. `Proc.Start()` for the quick one-liners 
   - Use if you want to capture the console output as well as print these message in real time.
   - Proc.Start() also allows you to script StandardIn and react to messages
3. Wraps `System.Diagnostics.Process` as an `IObservable` 
    * `ProcessObservable` stream based wrapper
    * `EventBasedObservableProcess` event based wrapper
4. Built in support to send `SIGINT` to any process before doing a hard `SIGKILL` (`Process.Kill()`)
    * Has to be set using `SendControlCFirst = true` on `StartArguments`
    
## Proc.Exec

Execute a process and blocks using a default timeout of 4 minutes. This method uses the same console session
as and as such will print the binaries console output. Throws a `ProcExecException` if the command fails to execute.
See also `ExecArguments` for more options

```csharp
Proc.Exec("ipconfig", "/all");
```

## Proc.Start

start a process and block using the default timeout of 4 minutes
```csharp
var result = Proc.Start("ipconfig", "/all");
```

Provide a custom timeout and an `IConsoleOutWriter` that can output to console 
while this line is blocking. The following example writes `stderr` in red.

```csharp
var result = Proc.Start("ipconfig", TimeSpan.FromSeconds(10), new ConsoleOutColorWriter());
```

More options can be passed by passing `StartArguments` instead to control how the process should start.

```csharp
var args = new StartArguments("ipconfig", "/all")
{
  WorkingDirectory = ..
}
Proc.Start(args, TimeSpan.FromSeconds(10));
```

The static  `Proc.Start` has a timeout of `4 minutes` if not specified.

`result` has the following properties

* `Completed` true if the program completed before the timeout
* `ConsoleOut` a list the console out message as `LineOut` 
   instances where `Error` on each indicating whether it was written on `stderr` or not
* `ExitCode` 

**NOTE** `ConsoleOut` will always be set regardless of whether an `IConsoleOutWriter` is provided

## ObservableProcess

The heart of it all this is an `IObservable<CharactersOut>`. It listens on the output buffers directly and does not wait on 
newlines to emit.

To create an observable process manually follow the following pattern:

```csharp
using (var p = new ObservableProcess(args))
{
	p.Subscribe(c => Console.Write(c.Characters));
	p.WaitForCompletion(TimeSpan.FromSeconds(2));
}
```

The observable is `cold` untill subscribed and is not intended to be reused or subscribed to multiple times. If you need to 
share a subscription look into RX's `Publish`.

The `WaitForCompletion()` call blocks so that `p` is not disposed which would attempt to shutdown the started process.

The default for doing a shutdown is through `Process.Kill` this is a hard `SIGKILL` on the process.

The cool thing about `Proc` is that it supports `SIGINT` interoptions as well to allow for processes to be cleanly shutdown. 

```csharp
var args = new StartArguments("elasticsearch.bat")
{
	SendControlCFirst = true
};
```

This will attempt to send a `Control+C` into the running process console on windows first before falling back to `Process.Kill`. 
Linux and OSX support for this flag is still in the works so thats why this behaviour is opt in.


Dealing with `byte[]` characters might not be what you want to program against, so `ObservableProcess` allows the following as well.


```csharp
using (var p = new ObservableProcess(args))
{
	p.SubscribeLines(c => Console.WriteLine(c.Line));
	p.WaitForCompletion(TimeSpan.FromSeconds(2));
}
```

Instead of proxying `byte[]` as they are received on the socket this buffers and only emits on lines. 

In some cases it can be very useful to introduce your own word boundaries

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

returning true inside `BufferBoundary` will yield the line to `SubscribeLine()`. This could be usefull e.g if your process 
prompts without a new line:

> Continue [Y/N]: <no newline here>

A more concrete example of this is when you call a `bat` file on windows and send a `SIGINT` signal it will *always* prompt:

> Terminate batch job (Y/N)?

Which would not yield to `SubscribeLines` and block any waithandles unnecessary. `ObservableProcess` handles this edgecase
therefor OOTB and automatically replies with `Y` on `stdin` in this case.

Also note that `ObservableProcess` will yield whatever is in the buffer before OnCompleted().


# EventBasedObservable

`ObservableProcess`'s sibbling that utilizes `OutputDataReceived` and `ErrorDataReceived` and can only emit lines.















