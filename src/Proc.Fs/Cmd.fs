namespace Proc.Fs

open System
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open ProcNet
open ProcNet.Std

/// <summary>What a command reads on its standard input</summary>
[<RequireQualifiedAccess>]
type StdinSource =
    /// <summary>Write these lines, then close standard input</summary>
    | Lines of string seq
    /// <summary>Stream the standard output of another command into this one, like a shell pipe</summary>
    | Upstream of Cmd

/// <summary>
/// A command to run: the binary, its arguments and how to run it. Nothing runs until the command is passed to
/// <c>Cmd.run</c>, <c>Cmd.get</c>, <c>Cmd.iter</c>, a <c>shell { }</c> or an <c>exec { }</c> block.
/// </summary>
and Cmd = {
    Binary: string
    Arguments: string list option
    LineOutFilter: (LineOut -> bool) option
    WorkingDirectory: string option
    Environment: Map<string, string> option

    /// <summary>How long to wait for the process to exit. <c>None</c> waits indefinitely.</summary>
    Timeout: TimeSpan option
    ValidExitCodeClassifier: (int -> bool) option

    StartedConfirmationHandler: (LineOut -> bool) option
    StopBufferingAfterStarted: bool option

    SendControlCFirst: bool option
    WaitForStreamReadersTimeout: TimeSpan option
    /// <summary>Terminate the started process when this process exits (.NET 11+ on Windows and Linux only)</summary>
    KillOnParentExit: bool option

    /// <summary>What to write to the standard input of the process. <c>None</c> closes it straight away.</summary>
    Stdin: StdinSource option
}
with
    static member Empty =
        {
            Binary = ""; Arguments = None
            LineOutFilter = None; WorkingDirectory = None; Environment = None
            Timeout = None
            ValidExitCodeClassifier = None
            SendControlCFirst = None; WaitForStreamReadersTimeout = None
            StartedConfirmationHandler = None; StopBufferingAfterStarted = None
            KillOnParentExit = None
            Stdin = None
        }

/// <summary>The options an <c>exec { }</c> block builds. The same type as <see cref="Cmd"/>.</summary>
type ExecOptions = Cmd

/// <summary>
/// A command plus how to read its output. Nothing runs until it is passed to <c>Cmd.get</c> or bound with
/// <c>let!</c> inside a <c>shell { }</c> block.
/// </summary>
type Capture<'T> = {
    Cmd: Cmd
    /// <summary>Also print standard out to the console while capturing</summary>
    EchoStdout: bool
    Read: ProcessCaptureResult -> 'T
}

/// Translates a Cmd into the argument types of the C# library
module internal Args =
    let private applyBase (cmd: Cmd) (a: ProcessArgumentsBase) =
        cmd.Environment |> Option.iter(fun e -> a.Environment <- e)
        cmd.WorkingDirectory |> Option.iter(fun d -> a.WorkingDirectory <- d)
        cmd.KillOnParentExit |> Option.iter(fun k -> a.KillOnParentExit <- k)

    let private applyStart (cmd: Cmd) (a: StartArguments) =
        applyBase cmd a
        cmd.LineOutFilter |> Option.iter(fun f -> a.LineOutFilter <- f)
        cmd.SendControlCFirst |> Option.iter(fun b -> a.SendControlCFirst <- b)
        cmd.WaitForStreamReadersTimeout |> Option.iter(fun t -> a.WaitForStreamReadersTimeout <- t)
        cmd.Timeout |> Option.iter(fun t -> a.Timeout <- Nullable t)

    let private arguments (cmd: Cmd) = cmd.Arguments |> Option.defaultValue []

    let private ensureNoStdin (cmd: Cmd) =
        if cmd.Stdin.IsSome then
            invalidArg (nameof cmd) $"exec {{ }} can not feed standard input to '{cmd.Binary}'. Run piped commands with Cmd.run, Cmd.get or shell {{ }}."

    /// StartArguments without a standard input handler; the caller decides what happens to stdin
    let startRaw (cmd: Cmd) =
        let startArguments = StartArguments(cmd.Binary, arguments cmd)
        applyStart cmd startArguments
        startArguments

    /// StartArguments whose standard input is closed straight away.
    /// Proc always redirects standard input, so a tool that reads it (grep, sort, cat) would otherwise wait forever.
    let start (cmd: Cmd) =
        ensureNoStdin cmd
        let startArguments = startRaw cmd
        startArguments.StandardInputHandler <- StandardInputHandler(fun stdin -> stdin.Close())
        startArguments

    let exec (cmd: Cmd) =
        ensureNoStdin cmd
        let execArguments = ExecArguments(cmd.Binary, arguments cmd)
        applyBase cmd execArguments
        cmd.ValidExitCodeClassifier |> Option.iter(fun f -> execArguments.ValidExitCodeClassifier <- f)
        cmd.Timeout |> Option.iter(fun t -> execArguments.Timeout <- Nullable t)
        execArguments

    let longRunning (cmd: Cmd) =
        ensureNoStdin cmd
        let longRunningArguments = LongRunningArguments(cmd.Binary, arguments cmd)
        applyBase cmd longRunningArguments
        cmd.LineOutFilter |> Option.iter(fun f -> longRunningArguments.LineOutFilter <- f)
        cmd.SendControlCFirst |> Option.iter(fun b -> longRunningArguments.SendControlCFirst <- b)
        cmd.WaitForStreamReadersTimeout |> Option.iter(fun t -> longRunningArguments.WaitForStreamReadersTimeout <- t)
        cmd.StartedConfirmationHandler |> Option.iter(fun t -> longRunningArguments.StartedConfirmationHandler <- t)
        cmd.StopBufferingAfterStarted |> Option.iter(fun t -> longRunningArguments.StopBufferingAfterStarted <- t)
        longRunningArguments

    let startedConfirmationTimeout (cmd: Cmd) =
        cmd.Timeout |> Option.defaultValue (TimeSpan.FromMilliseconds -1.)

    /// Returns the exit code instead of throwing, unless the caller supplied a classifier of their own
    let acceptAnyExitCode (cmd: Cmd) =
        match cmd.ValidExitCodeClassifier with
        | Some _ -> cmd
        | None -> { cmd with ValidExitCodeClassifier = Some (fun _ -> true) }

/// Runs commands, including pipes, on top of Proc.StartRedirected
module internal Runner =

    /// Quotes arguments containing spaces, the same way Proc does in its exception messages
    let private quote (arg: string) =
        if arg.Contains " " && not (arg.StartsWith "\"") then $"\"{arg}\"" else arg

    /// "bin args | bin args", used in exception messages and when a shell echoes commands
    let rec describe (cmd: Cmd) =
        let self =
            match cmd.Arguments with
            | None | Some [] -> cmd.Binary
            | Some args -> cmd.Binary + " " + String.Join(" ", args |> List.map quote)
        match cmd.Stdin with
        | Some (StdinSource.Upstream upstream) -> $"{describe upstream} | {self}"
        | Some (StdinSource.Lines _) -> $"<stdin> | {self}"
        | None -> self

    /// Throws ProcExecException when the process timed out or exited with an exit code the command does not accept
    let validate (cmd: Cmd) (result: ProcessResult) =
        if not result.Completed then
            let timeout = cmd.Timeout |> Option.map string |> Option.defaultValue "?"
            raise (ProcExecException($"Timeout {timeout} occured while running \"{describe cmd}\""))
        let isValid = cmd.ValidExitCodeClassifier |> Option.defaultValue (fun c -> c = 0)
        if not result.ExitCode.HasValue then
            raise (ProcExecException($"Process did not report an exit code \"{describe cmd}\""))
        let exitCode = result.ExitCode.Value
        if not (isValid exitCode) then
            raise (ProcExecException($"Process exited with '{exitCode}' \"{describe cmd}\"", ExitCode = Nullable exitCode))

    let private lineHandler (onLine: LineOut -> unit) =
        { new IConsoleLineHandler with
            member _.Handle(line: LineOut) = onLine line
            // StartRedirected rethrows the exception once the process completes
            member _.Handle(_: Exception) = () }

    let printLine (line: LineOut) =
        if line.Error then Console.Error.WriteLine line.Line else Console.Out.WriteLine line.Line

    let private printStderr (line: LineOut) =
        if line.Error then Console.Error.WriteLine line.Line

    /// <summary>
    /// Runs <paramref name="cmd"/> and calls <paramref name="onLine"/> for every line it prints, as it prints it.
    /// Does not validate the exit code of <paramref name="cmd"/> itself, but does validate every upstream command.
    /// </summary>
    let rec stream (cmd: Cmd) (ct: CancellationToken) (onLine: LineOut -> unit) : ProcessResult =
        let args = Args.startRaw cmd
        match cmd.Stdin with
        | None ->
            Proc.StartRedirected(args, StandardInputHandler(fun stdin -> stdin.Close()), lineHandler onLine, ct)
        | Some source ->
            // Proc raises StandardInputReady before it reads the output of the process. Writing stdin from that
            // callback could fill the output pipe of the process and deadlock, so a background task does the writing.
            let ready = TaskCompletionSource<StreamWriter>(TaskCreationOptions.RunContinuationsAsynchronously)
            use feedCts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            let feeder =
                Task.Run(Action(fun () ->
                    let stdin = ready.Task.Result
                    try feed source stdin feedCts
                    finally (try stdin.Close() with _ -> ())))
            let result =
                try Proc.StartRedirected(args, StandardInputHandler(fun stdin -> ready.TrySetResult stdin |> ignore), lineHandler onLine, ct)
                finally
                    ready.TrySetCanceled() |> ignore
                    // The process has exited, so nothing reads stdin any more: stop whatever is still feeding it
                    feedCts.Cancel()
            try feeder.Wait()
            with :? AggregateException as e when not (isNull e.InnerException) ->
                ExceptionDispatchInfo.Capture(e.InnerException).Throw()
            result

    /// Writes the source into stdin. Stops, without failing, when the reading process exits early.
    and private feed (source: StdinSource) (stdin: StreamWriter) (feedCts: CancellationTokenSource) =
        let write (line: string) =
            if not feedCts.IsCancellationRequested then
                try stdin.WriteLine line
                with
                | :? IOException
                | :? ObjectDisposedException -> feedCts.Cancel()
        match source with
        | StdinSource.Lines lines ->
            use e = lines.GetEnumerator()
            while not feedCts.IsCancellationRequested && e.MoveNext() do write e.Current
        | StdinSource.Upstream upstream ->
            let completed =
                try
                    Some (stream upstream feedCts.Token (fun line -> if line.Error then printStderr line else write line.Line))
                // The downstream process exited before reading everything: like a shell, that is not a failure
                with :? OperationCanceledException when feedCts.IsCancellationRequested -> None
            completed |> Option.iter (validate upstream)

    /// Runs the command on the shared console and throws when it fails
    let run (cmd: Cmd) =
        match cmd.Stdin with
        | None -> Proc.Exec(Args.exec cmd) |> ignore
        | Some _ -> stream cmd CancellationToken.None printLine |> validate cmd

    /// Calls f for each line of standard out as it is printed; standard error goes to the console
    let iter (f: string -> unit) (cmd: Cmd) =
        let mutable error: exn = null
        use cts = new CancellationTokenSource()
        let onLine (line: LineOut) =
            if line.Error then printStderr line
            elif isNull error then
                try f line.Line
                with e ->
                    error <- e
                    cts.Cancel()
        let result =
            try Some (stream cmd cts.Token onLine)
            with :? OperationCanceledException when not (isNull error) -> None
        if not (isNull error) then ExceptionDispatchInfo.Capture(error).Throw()
        result |> Option.iter (validate cmd)

    /// Runs the command, collecting every line, and reads the result
    let get (capture: Capture<'T>) : 'T =
        let lines = ResizeArray<LineOut>()
        let onLine (line: LineOut) =
            lock lines (fun () -> lines.Add line)
            if line.Error || capture.EchoStdout then printLine line
        let result = stream capture.Cmd CancellationToken.None onLine
        validate capture.Cmd result
        capture.Read (ProcessCaptureResult(result.Completed, lines, result.ExitCode))

/// <summary>Functions to build, combine and run <see cref="Cmd"/> values; they read well with <c>|&gt;</c></summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Cmd =

    /// <summary>A command running <paramref name="binary"/> with <paramref name="arguments"/></summary>
    let create (binary: string) (arguments: string list) =
        { Cmd.Empty with Binary = binary; Arguments = Some arguments }

    /// <summary>Appends arguments</summary>
    let args (arguments: string list) (cmd: Cmd) =
        { cmd with Arguments = Some ((cmd.Arguments |> Option.defaultValue []) @ arguments) }

    /// <summary>Runs the command in <paramref name="directory"/></summary>
    let cwd (directory: string) (cmd: Cmd) = { cmd with WorkingDirectory = Some directory }

    /// <summary>Adds environment variables, scoped to the process; later values win</summary>
    let env (variables: (string * string) list) (cmd: Cmd) =
        let current = cmd.Environment |> Option.defaultValue Map.empty
        { cmd with Environment = Some (variables |> List.fold (fun m (k, v) -> Map.add k v m) current) }

    /// <summary>Kills the process when it runs longer than <paramref name="timeout"/></summary>
    let timeout (timeout: TimeSpan) (cmd: Cmd) = { cmd with Timeout = Some timeout }

    /// <summary>Decides which exit codes count as success. Defaults to only <c>0</c></summary>
    let validExitCode (isValid: int -> bool) (cmd: Cmd) = { cmd with ValidExitCodeClassifier = Some isValid }

    /// <summary>Terminates the process when this process exits (.NET 11+ on Windows and Linux only)</summary>
    let killOnParentExit (cmd: Cmd) = { cmd with KillOnParentExit = Some true }

    /// <summary>Writes <paramref name="lines"/> to the standard input of the process, then closes it</summary>
    let stdin (lines: string seq) (cmd: Cmd) = { cmd with Stdin = Some (StdinSource.Lines lines) }

    /// <summary>
    /// Streams the standard out of <paramref name="upstream"/> into the standard input of <paramref name="downstream"/>.
    /// Standard error of <paramref name="upstream"/> goes to the console. Prefer the <c>|&gt;|</c> operator.
    /// </summary>
    let pipe (downstream: Cmd) (upstream: Cmd) = { downstream with Stdin = Some (StdinSource.Upstream upstream) }

    /// <summary>Captures the lines the command writes to standard out. Fails when the command fails.</summary>
    let lines (cmd: Cmd) : Capture<string list> =
        { Cmd = cmd; EchoStdout = false
          Read = fun r -> r.ConsoleOut |> Seq.filter (fun l -> not l.Error) |> Seq.map (fun l -> l.Line) |> List.ofSeq }

    /// <summary>Captures standard out as one string, lines joined with <c>\n</c> and surrounding whitespace trimmed</summary>
    let text (cmd: Cmd) : Capture<string> =
        let lines = lines cmd
        { Cmd = cmd; EchoStdout = false; Read = fun r -> (String.Join("\n", lines.Read r)).Trim() }

    /// <summary>Runs the command, printing its output, and returns the exit code without throwing on a non-zero one</summary>
    let exitCode (cmd: Cmd) : Capture<int> =
        { Cmd = Args.acceptAnyExitCode cmd; EchoStdout = true; Read = fun r -> r.ExitCode.Value }

    /// <summary>Captures every line plus the exit code, without throwing on a non-zero exit code</summary>
    let result (cmd: Cmd) : Capture<ProcessCaptureResult> =
        { Cmd = Args.acceptAnyExitCode cmd; EchoStdout = false; Read = id }

    /// <summary>Transforms what a capture returns</summary>
    let mapCapture (f: 'T -> 'U) (capture: Capture<'T>) : Capture<'U> =
        { Cmd = capture.Cmd; EchoStdout = capture.EchoStdout; Read = capture.Read >> f }

    /// <summary>Keeps the captured lines matching <paramref name="predicate"/></summary>
    let where (predicate: string -> bool) (capture: Capture<string list>) = mapCapture (List.filter predicate) capture

    /// <summary>Transforms each captured line</summary>
    let map (f: string -> 'U) (capture: Capture<string list>) = mapCapture (List.map f) capture

    /// <summary>Transforms each captured line, dropping it when <paramref name="f"/> returns <c>None</c></summary>
    let choose (f: string -> 'U option) (capture: Capture<string list>) = mapCapture (List.choose f) capture

    /// <summary>Runs the command on the shared console. Throws <see cref="ProcExecException"/> when it fails.</summary>
    let run (cmd: Cmd) = Runner.run cmd

    /// <summary>Runs the command and returns what the capture reads</summary>
    let get (capture: Capture<'T>) : 'T = Runner.get capture

    /// <summary>
    /// Calls <paramref name="f"/> for each line of standard out while the command runs, without buffering.
    /// Standard error goes to the console. Throws when the command fails or <paramref name="f"/> throws.
    /// </summary>
    let iter (f: string -> unit) (cmd: Cmd) = Runner.iter f cmd

/// <summary>Brought into scope by <c>open Proc.Fs</c></summary>
[<AutoOpen>]
module CmdOperators =

    /// <summary>A command running <paramref name="binary"/> with <paramref name="arguments"/></summary>
    let cmd (binary: string) (arguments: string list) = Cmd.create binary arguments

    /// <summary>
    /// Pipes the standard out of the left command into the standard input of the right one, like <c>|</c> in a shell.
    /// Every command in the pipe must succeed. Pipes carry lines of text, not raw bytes.
    /// </summary>
    let (|>|) (upstream: Cmd) (downstream: Cmd) = Cmd.pipe downstream upstream
