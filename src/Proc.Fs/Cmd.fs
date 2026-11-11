namespace Proc.Fs

open System
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open ProcNet
open ProcNet.Std

/// <summary>Where a command writes its standard out instead of the console</summary>
[<RequireQualifiedAccess>]
type StdoutTarget =
    /// <summary>Write to this file, replacing it, like <c>&gt; file</c>. A relative path resolves against the working directory of the command.</summary>
    | File of path: string
    /// <summary>Add to the end of this file, like <c>&gt;&gt; file</c>. A relative path resolves against the working directory of the command.</summary>
    | Append of path: string

/// <summary>How often to run a failing command again</summary>
type RetryPolicy = {
    /// <summary>How many more times to run the command after it fails</summary>
    Retries: int
    /// <summary>How long to wait before each new attempt</summary>
    Delay: TimeSpan
}

/// <summary>What a command reads on its standard input</summary>
[<RequireQualifiedAccess>]
type StdinSource =
    /// <summary>Write these lines, then close standard input</summary>
    | Lines of string seq
    /// <summary>Stream the standard output of another command into this one, like a shell pipe</summary>
    | Upstream of Cmd
    /// <summary>Read this file, like <c>&lt; file</c>. A relative path resolves against the working directory of the command.</summary>
    | File of path: string

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
    /// <summary>Where standard out goes instead of the console or a capture. <c>None</c> keeps it.</summary>
    Stdout: StdoutTarget option
    /// <summary>Hide everything the command prints unless it fails</summary>
    Quiet: bool option
    /// <summary>Run the command again when it fails</summary>
    Retry: RetryPolicy option
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
            Stdin = None; Stdout = None; Quiet = None; Retry = None
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

    /// Proc.Exec and Proc.Start can not do these; Runner can
    let private ensureNoStdin (cmd: Cmd) =
        let unsupported =
            [ if cmd.Stdin.IsSome then "reads standard input"
              if cmd.Stdout.IsSome then "writes standard out to a file"
              if cmd.Quiet = Some true then "is quiet"
              if cmd.Retry.IsSome then "retries" ]
        if not unsupported.IsEmpty then
            let what = String.Join(" and ", unsupported)
            invalidArg (nameof cmd) $"'{cmd.Binary}' {what}, which only exec {{ run }}, Cmd.run, Cmd.get, Cmd.iter and shell {{ }} support."

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
        let self =
            match cmd.Stdout with
            | Some (StdoutTarget.File path) -> $"{self} > {quote path}"
            | Some (StdoutTarget.Append path) -> $"{self} >> {quote path}"
            | None -> self
        match cmd.Stdin with
        | Some (StdinSource.Upstream upstream) -> $"{describe upstream} | {self}"
        | Some (StdinSource.Lines _) -> $"<stdin> | {self}"
        | Some (StdinSource.File path) -> $"{self} < {quote path}"
        | None -> self

    /// A path relative to the working directory of the command, as the process itself would see it
    let resolve (cmd: Cmd) (path: string) =
        let directory = cmd.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory
        Path.GetFullPath(Path.Combine(directory, path))

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

    /// <summary>
    /// Runs <paramref name="body"/> with where the command prints to. A quiet command prints into a buffer instead,
    /// and the buffer goes to <paramref name="console"/> only when <paramref name="body"/> throws.
    /// </summary>
    let withConsole (cmd: Cmd) (console: LineOut -> unit) (body: (LineOut -> unit) -> 'T) : 'T =
        if cmd.Quiet <> Some true then body console
        else
            let buffer = ResizeArray<LineOut>()
            try body (fun line -> lock buffer (fun () -> buffer.Add line))
            with _ ->
                lock buffer (fun () -> for line in buffer do console line)
                reraise ()

    /// Runs attempt again while it throws ProcExecException and the command's retry policy allows.
    /// Stops retrying once ct is cancelled.
    let retrying (cmd: Cmd) (ct: CancellationToken) (attempt: unit -> 'T) : 'T =
        let policy = cmd.Retry |> Option.defaultValue { Retries = 0; Delay = TimeSpan.Zero }
        let rec go tries =
            try attempt ()
            with :? ProcExecException as e when tries < policy.Retries && not ct.IsCancellationRequested ->
                if cmd.Quiet <> Some true then
                    Console.Error.WriteLine $"Retrying ({tries + 1} of {policy.Retries}) after: {e.Message}"
                if policy.Delay > TimeSpan.Zero then
                    if ct.CanBeCanceled then ct.WaitHandle.WaitOne policy.Delay |> ignore else Thread.Sleep policy.Delay
                ct.ThrowIfCancellationRequested()
                go (tries + 1)
        go 0

    /// <summary>
    /// Runs <paramref name="cmd"/> and calls <paramref name="onLine"/> for every line it prints, as it prints it.
    /// Standard out goes to the file of <c>cmd.Stdout</c> instead, when it has one.
    /// Upstream commands print their standard error to <paramref name="console"/>.
    /// Does not validate the exit code of <paramref name="cmd"/> itself, but does validate every upstream command.
    /// </summary>
    let rec stream (cmd: Cmd) (ct: CancellationToken) (console: LineOut -> unit) (onLine: LineOut -> unit) : ProcessResult =
        let args = Args.startRaw cmd
        // Open files before the process starts, so a missing one fails before anything runs
        use stdoutFile =
            match cmd.Stdout with
            | Some (StdoutTarget.File path) -> new StreamWriter(resolve cmd path, false)
            | Some (StdoutTarget.Append path) -> new StreamWriter(resolve cmd path, true)
            | None -> null
        let onLine =
            match stdoutFile with
            | null -> onLine
            | file -> fun (line: LineOut) -> if line.Error then onLine line else lock file (fun () -> file.WriteLine line.Line)
        use stdinFile =
            match cmd.Stdin with
            | Some (StdinSource.File path) -> new StreamReader(resolve cmd path)
            | _ -> null
        let source =
            match cmd.Stdin with
            | Some (StdinSource.File _) ->
                Some (StdinSource.Lines (Seq.unfold (fun () -> match stdinFile.ReadLine() with null -> None | l -> Some (l, ())) ()))
            | other -> other
        match source with
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
                    try feed source stdin feedCts console
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
    and private feed (source: StdinSource) (stdin: StreamWriter) (feedCts: CancellationTokenSource) (console: LineOut -> unit) =
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
        | StdinSource.File _ -> invalidOp "stream reads the file and feeds its lines"
        | StdinSource.Upstream upstream ->
            withConsole upstream console (fun console ->
                let completed =
                    try
                        Some (stream upstream feedCts.Token console (fun line -> if line.Error then console line else write line.Line))
                    // The downstream process exited before reading everything: like a shell, that is not a failure
                    with :? OperationCanceledException when feedCts.IsCancellationRequested -> None
                completed |> Option.iter (validate upstream))

    /// Runs the command on the shared console and throws when it fails.
    /// Cancelling ct stops the process and throws OperationCanceledException.
    let run (ct: CancellationToken) (cmd: Cmd) =
        retrying cmd ct (fun () ->
            match cmd with
            // Proc.Exec takes no cancellation token, so a cancellable run streams instead
            | { Stdin = None; Stdout = None; Quiet = (None | Some false) } when not ct.CanBeCanceled ->
                Proc.Exec(Args.exec { cmd with Retry = None }) |> ignore
            | _ ->
                withConsole cmd printLine (fun console ->
                    stream cmd ct console console |> validate cmd))

    /// Calls f for each line of standard out as it is printed; standard error goes to the console
    let iter (ct: CancellationToken) (f: string -> unit) (cmd: Cmd) =
        retrying cmd ct (fun () ->
            withConsole cmd printLine (fun console ->
                let mutable error: exn = null
                use cts = CancellationTokenSource.CreateLinkedTokenSource ct
                let onLine (line: LineOut) =
                    if line.Error then console line
                    elif isNull error then
                        try f line.Line
                        with e ->
                            error <- e
                            cts.Cancel()
                let result =
                    try Some (stream cmd cts.Token console onLine)
                    with :? OperationCanceledException when not (isNull error) -> None
                if not (isNull error) then ExceptionDispatchInfo.Capture(error).Throw()
                result |> Option.iter (validate cmd)))

    /// Runs the command, collecting every line, and reads the result
    let get (ct: CancellationToken) (capture: Capture<'T>) : 'T =
        retrying capture.Cmd ct (fun () ->
            withConsole capture.Cmd printLine (fun console ->
                let lines = ResizeArray<LineOut>()
                let onLine (line: LineOut) =
                    lock lines (fun () -> lines.Add line)
                    if line.Error || capture.EchoStdout then console line
                let result = stream capture.Cmd ct console onLine
                validate capture.Cmd result
                capture.Read (ProcessCaptureResult(result.Completed, lines, result.ExitCode))))

/// Finds programs on the PATH, like the which command
module internal Which =
    let private isExecutable (path: string) =
        File.Exists path
        && (OperatingSystem.IsWindows()
            || File.GetUnixFileMode path &&& (UnixFileMode.UserExecute ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherExecute) <> UnixFileMode.None)

    /// <summary>
    /// The full path of <paramref name="name"/>: a path when it holds a directory separator, else the first match
    /// in <paramref name="path"/>. On Windows a name without an extension also tries each extension in PATHEXT.
    /// </summary>
    let find (baseDirectory: string) (path: string option) (pathExt: string option) (name: string) : string option =
        let candidates (file: string) =
            if OperatingSystem.IsWindows() && not (Path.HasExtension file) then
                let extensions = pathExt |> Option.defaultValue ".COM;.EXE;.BAT;.CMD"
                [ for ext in extensions.Split(';', StringSplitOptions.RemoveEmptyEntries) -> file + ext ]
            else [ file ]
        if String.IsNullOrWhiteSpace name then None
        elif name.Contains '/' || name.Contains Path.DirectorySeparatorChar then
            candidates (Path.GetFullPath(Path.Combine(baseDirectory, name))) |> List.tryFind isExecutable
        else
            let directories = (path |> Option.defaultValue "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            seq {
                for directory in directories do
                    let directory = directory.Trim('"')
                    if Path.IsPathFullyQualified directory then
                        for candidate in candidates (Path.Combine(directory, name)) -> candidate
            }
            |> Seq.tryFind isExecutable

/// <summary>Functions to build, combine and run <see cref="Cmd"/> values; they read well with <c>|&gt;</c></summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Cmd =

    /// <summary>A command running <paramref name="binary"/> with <paramref name="arguments"/></summary>
    let create (binary: string) (arguments: string list) =
        { Cmd.Empty with Binary = binary; Arguments = Some arguments }

    /// <summary>
    /// Streams the standard out of <paramref name="upstream"/> into the standard input of <paramref name="downstream"/>.
    /// Standard error of <paramref name="upstream"/> goes to the console. Prefer the <c>|&gt;|</c> operator.
    /// </summary>
    let pipe (downstream: Cmd) (upstream: Cmd) = { downstream with Stdin = Some (StdinSource.Upstream upstream) }

    /// One command per stage, piped together, with the redirects of the text
    let internal ofParsed (parsed: ShellSyntax.Parsed) =
        let stages = parsed.Stages |> List.map (fun stage -> create (List.head stage) (List.tail stage))
        let stages =
            match parsed.StdinFile, stages with
            | Some file, first :: rest -> { first with Stdin = Some (StdinSource.File file) } :: rest
            | _ -> stages
        let piped = stages |> List.reduce (fun upstream downstream -> pipe downstream upstream)
        match parsed.StdoutFile with
        | Some (file, false) -> { piped with Stdout = Some (StdoutTarget.File file) }
        | Some (file, true) -> { piped with Stdout = Some (StdoutTarget.Append file) }
        | None -> piped

    /// <summary>
    /// Parses command text that only exists at runtime, such as a value read from configuration.
    /// The whole string is split like the literal text of <c>sh</c>, so prefer <c>sh $"…"</c> for values.
    /// </summary>
    let ofString (command: string) =
        ShellSyntax.parse command [ ShellSyntax.Text command ] |> ofParsed

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

    /// <summary>
    /// For <c>background</c> in a <c>shell { }</c> block: wait until the command prints a line, on standard out or
    /// standard error, for which <paramref name="isReady"/> is true. <c>Cmd.timeout</c> limits that wait.
    /// </summary>
    let waitFor (isReady: string -> bool) (cmd: Cmd) =
        { cmd with StartedConfirmationHandler = Some (fun (line: LineOut) -> isReady line.Line) }

    /// <summary>Writes <paramref name="lines"/> to the standard input of the process, then closes it</summary>
    let stdin (lines: string seq) (cmd: Cmd) = { cmd with Stdin = Some (StdinSource.Lines lines) }

    /// <summary>
    /// Reads standard input from <paramref name="path"/>, like <c>&lt; file</c>.
    /// A relative path resolves against the working directory of the command.
    /// </summary>
    let stdinFile (path: string) (cmd: Cmd) = { cmd with Stdin = Some (StdinSource.File path) }

    /// <summary>
    /// Writes standard out to <paramref name="path"/>, replacing the file, like <c>&gt; file</c>. Standard error
    /// still goes to the console. A relative path resolves against the working directory of the command.
    /// A capture such as <c>Cmd.lines</c> then reads no lines, as in a shell.
    /// </summary>
    let toFile (path: string) (cmd: Cmd) = { cmd with Stdout = Some (StdoutTarget.File path) }

    /// <summary>Like <c>toFile</c>, but adds to the end of the file, like <c>&gt;&gt; file</c></summary>
    let appendToFile (path: string) (cmd: Cmd) = { cmd with Stdout = Some (StdoutTarget.Append path) }

    /// <summary>
    /// Accepts any exit code from the command and from every command piped into it, like <c>|| true</c>.
    /// A timeout still throws.
    /// </summary>
    let rec allowFailure (cmd: Cmd) =
        let stdin =
            match cmd.Stdin with
            | Some (StdinSource.Upstream upstream) -> Some (StdinSource.Upstream (allowFailure upstream))
            | other -> other
        { cmd with ValidExitCodeClassifier = Some (fun _ -> true); Stdin = stdin }

    /// <summary>
    /// Hides what the command, and every command piped into it, prints. When it fails, everything it printed
    /// goes to the console before the exception, so CI logs show only what went wrong.
    /// </summary>
    let quiet (cmd: Cmd) = { cmd with Quiet = Some true }

    /// <summary>
    /// Runs the command up to <paramref name="retries"/> more times while it fails. <c>Cmd.retry 2</c> runs it at
    /// most three times. A pipe runs again as a whole. Lines <c>Cmd.iter</c> already passed on are passed on again.
    /// </summary>
    let retry (retries: int) (cmd: Cmd) =
        if retries < 0 then invalidArg (nameof retries) "can not be negative"
        let delay = cmd.Retry |> Option.map (fun r -> r.Delay) |> Option.defaultValue TimeSpan.Zero
        { cmd with Retry = Some { Retries = retries; Delay = delay } }

    /// <summary>Waits <paramref name="delay"/> before each new attempt of <c>Cmd.retry</c></summary>
    let retryDelay (delay: TimeSpan) (cmd: Cmd) =
        let retries = cmd.Retry |> Option.map (fun r -> r.Retries) |> Option.defaultValue 0
        { cmd with Retry = Some { Retries = retries; Delay = delay } }

    /// <summary>
    /// The full path of the program <paramref name="name"/> on the PATH, or <c>None</c>.
    /// On Windows a name without an extension also tries the extensions in PATHEXT.
    /// </summary>
    let which (name: string) : string option =
        Which.find (Directory.GetCurrentDirectory()) (Option.ofObj (Environment.GetEnvironmentVariable "PATH"))
            (Option.ofObj (Environment.GetEnvironmentVariable "PATHEXT")) name

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

    /// <summary>Runs the command, printing its output, and returns whether it exited with <c>0</c>, without throwing</summary>
    let succeeds (cmd: Cmd) : Capture<bool> =
        { Cmd = Args.acceptAnyExitCode cmd; EchoStdout = true; Read = fun r -> r.ExitCode.Value = 0 }

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
    let run (cmd: Cmd) = Runner.run CancellationToken.None cmd

    /// <summary>Runs the command and returns what the capture reads</summary>
    let get (capture: Capture<'T>) : 'T = Runner.get CancellationToken.None capture

    /// <summary>
    /// Calls <paramref name="f"/> for each line of standard out while the command runs, without buffering.
    /// Standard error goes to the console. Throws when the command fails or <paramref name="f"/> throws.
    /// </summary>
    let iter (f: string -> unit) (cmd: Cmd) = Runner.iter CancellationToken.None f cmd

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

    /// <summary>
    /// A command written as one interpolated string: <c>sh $"wc -l {file}"</c>.
    /// <para>
    /// The text you write splits on whitespace; <c>'…'</c> and <c>"…"</c> group words; an unquoted <c>|</c> pipes into
    /// the next command; a <c>\</c> at the end of a line continues the command. Each <c>{value}</c> is always part of
    /// exactly one argument, whatever it contains. A list standing alone, such as <c>{files}</c>, adds one argument
    /// per item. No shell runs, so values can not start other commands.
    /// </para>
    /// <para>A plain string without <c>$</c> does not compile; use <c>Cmd.ofString</c> for text built at runtime.</para>
    /// </summary>
    let sh (command: FormattableString) : Cmd =
        let segments = ShellSyntax.segmentsOf command.Format (command.GetArguments())
        ShellSyntax.parse command.Format segments |> Cmd.ofParsed

    /// <summary>Runs <c>sh</c> and captures standard out as one trimmed string, for <c>let!</c> or <c>Cmd.get</c></summary>
    let text (command: FormattableString) = sh command |> Cmd.text

    /// <summary>Runs <c>sh</c> and captures the lines of standard out, for <c>let!</c> or <c>Cmd.get</c></summary>
    let lines (command: FormattableString) = sh command |> Cmd.lines

    /// <summary>Runs <c>sh</c>, printing its output, and returns the exit code without throwing</summary>
    let exitCode (command: FormattableString) = sh command |> Cmd.exitCode

    /// <summary>Runs <c>sh</c>, printing its output, and returns whether it exited with <c>0</c>, without throwing</summary>
    let succeeds (command: FormattableString) = sh command |> Cmd.succeeds
