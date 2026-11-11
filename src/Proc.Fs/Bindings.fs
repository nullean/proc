module Proc.Fs

open System
open ProcNet
open ProcNet.Std

type ExecOptions = {
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
        }

let private applyBase (opts: ExecOptions) (a: ProcessArgumentsBase) =
    opts.Environment |> Option.iter(fun e -> a.Environment <- e)
    opts.WorkingDirectory |> Option.iter(fun d -> a.WorkingDirectory <- d)
    opts.KillOnParentExit |> Option.iter(fun k -> a.KillOnParentExit <- k)

let private applyStart (opts: ExecOptions) (a: StartArguments) =
    applyBase opts a
    opts.LineOutFilter |> Option.iter(fun f -> a.LineOutFilter <- f)
    opts.SendControlCFirst |> Option.iter(fun b -> a.SendControlCFirst <- b)
    opts.WaitForStreamReadersTimeout |> Option.iter(fun t -> a.WaitForStreamReadersTimeout <- t)
    opts.Timeout |> Option.iter(fun t -> a.Timeout <- Nullable t)
    // Proc always redirects standard input. Nothing in these bindings writes to it, so close it straight away:
    // tools that read stdin (grep, sort, cat) then see end-of-input instead of waiting forever.
    a.StandardInputHandler <- StandardInputHandler(fun stdin -> stdin.Close())

let private startArgs (opts: ExecOptions) =
    let args = opts.Arguments |> Option.defaultValue []
    let startArguments = StartArguments(opts.Binary, args)
    applyStart opts startArguments
    startArguments

let private execArgs (opts: ExecOptions) =
    let args = opts.Arguments |> Option.defaultValue []
    let execArguments = ExecArguments(opts.Binary, args)
    applyBase opts execArguments
    opts.ValidExitCodeClassifier |> Option.iter(fun f -> execArguments.ValidExitCodeClassifier <- f)
    opts.Timeout |> Option.iter(fun t -> execArguments.Timeout <- Nullable t)
    execArguments

/// exit_code returns the code instead of throwing, unless the caller supplied a classifier of their own
let private acceptAnyExitCode (opts: ExecOptions) =
    match opts.ValidExitCodeClassifier with
    | Some _ -> opts
    | None -> { opts with ValidExitCodeClassifier = Some (fun _ -> true) }

let private longRunningArguments (opts: ExecOptions) =
    let args = opts.Arguments |> Option.defaultValue []
    let longRunningArguments = LongRunningArguments(opts.Binary, args)
    applyBase opts longRunningArguments
    opts.LineOutFilter |> Option.iter(fun f -> longRunningArguments.LineOutFilter <- f)
    opts.SendControlCFirst |> Option.iter(fun b -> longRunningArguments.SendControlCFirst <- b)
    opts.WaitForStreamReadersTimeout |> Option.iter(fun t -> longRunningArguments.WaitForStreamReadersTimeout <- t)

    opts.StartedConfirmationHandler |> Option.iter(fun t -> longRunningArguments.StartedConfirmationHandler <- t)
    opts.StopBufferingAfterStarted |> Option.iter(fun t -> longRunningArguments.StopBufferingAfterStarted <- t)

    longRunningArguments

let private startedConfirmationTimeout (opts: ExecOptions) =
    opts.Timeout |> Option.defaultValue (TimeSpan.FromMilliseconds -1.)
    

type ShellBuilder() =

    member t.Yield _ = ExecOptions.Empty
        
    [<CustomOperation("working_dir")>]
    member inline this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member inline this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    [<CustomOperation("timeout")>]
    member inline this.Timeout(opts, timeout: TimeSpan) =
        { opts with Timeout = Some timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member inline this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member inline this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<Obsolete("Proc no longer wraps processes in a thread; thread_wrap has no effect")>]
    [<CustomOperation("thread_wrap")>]
    member inline this.NoWrapInThread(opts: ExecOptions, _threadWrap: bool) = opts
        
    [<CustomOperation("exec")>]
    member this.ExecuteWithArguments(opts, binary, [<ParamArray>] args: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (args |> List.ofArray)  }
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        opts
        
    [<CustomOperation("exec")>]
    member this.ExecuteWithArguments(opts, binary, args: string list) =
        let opts = { opts with Binary = binary; Arguments = Some args }
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        opts
        
        
let shell = ShellBuilder()

type ExecBuilder() =

    member t.Yield _ = ExecOptions.Empty
        
    ///<summary>Runs <paramref name="binary"/> using <paramref name="arguments"/> immediately</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    /// <param name="arguments">the arguments to pass on to the binary being executed</param>
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    ///<summary>Runs <paramref name="binary"/> using <paramref name="arguments"/> immediately</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    /// <param name="arguments">the arguments to pass on to the binary being executed</param>
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    ///<summary>
    /// Runs the <see cref="ExecOptions"/> the computation build thus far.
    /// <para>Needs at least `binary` to be specified</para>
    /// </summary>
    [<CustomOperation("run")>]
    member this.Execute(opts) =
        if opts.Binary = "" then failwithf "No binary specified to exec computation expression"
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    ///<summary>Supply external <see cref="ExecOptions"/> to bootstrap</summary>
    [<CustomOperation("options")>]
    member this.Options(_, reuseOptions: ExecOptions) =
        reuseOptions
        
    ///<summary>The binary to execute</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    [<CustomOperation("binary")>]
    member this.Binary(opts, binary) =
        { opts with Binary = binary }
        
    ///<summary>The arguments to call the binary with</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="arguments">The arguments to call the binary with</param>
    [<CustomOperation("arguments")>]
    member this.Arguments(opts, [<ParamArray>] arguments: string array) =
        { opts with Arguments = Some (arguments |> List.ofArray) }
        
    ///<summary>The arguments to call the binary with</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="arguments">The arguments to call the binary with</param>
    [<CustomOperation("arguments")>]
    member this.Arguments(opts, arguments: string list) =
        { opts with Arguments = Some arguments}
        
    ///<summary>Specify a working directory to start the execution of the binary in</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="workingDirectory">Specify a working directory to start the execution of the binary in</param>
    [<CustomOperation("working_dir")>]
    member this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    ///<summary>How long to wait for the process to exit before killing it</summary>
    [<CustomOperation("timeout")>]
    member this.Timeout(opts, timeout: TimeSpan) =
        { opts with Timeout = Some timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<Obsolete("Proc no longer wraps processes in a thread; thread_wrap has no effect")>]
    [<CustomOperation("thread_wrap")>]
    member this.NoWrapInThread(opts: ExecOptions, _threadWrap: bool) = opts

    ///<summary>Terminate the process when this process exits (.NET 11+ on Windows and Linux only)</summary>
    [<CustomOperation("kill_on_parent_exit")>]
    member this.KillOnParentExit(opts, killOnParentExit: bool) =
        { opts with KillOnParentExit = Some killOnParentExit }
        
    [<CustomOperation("filter_output")>]
    member this.FilterOutput(opts, find: LineOut -> bool) =
        { opts with LineOutFilter = Some find }
        
    ///<summary>Decides which exit codes count as success. Defaults to only <c>0</c></summary>
    [<CustomOperation("valid_exit_code")>]
    member this.ValidExitCode(opts, exitCodeClassifier: int -> bool) =
        { opts with ValidExitCodeClassifier = Some exitCodeClassifier }

    [<Obsolete("Use valid_exit_code")>]
    [<CustomOperation("validExitCode")>]
    member this.ValidExitCodeLegacy(opts, exitCodeClassifier: int -> bool) =
        { opts with ValidExitCodeClassifier = Some exitCodeClassifier }

    [<CustomOperation("find")>]
    member this.Find(opts, find: LineOut -> bool) =
        let startArguments = startArgs opts
        let result = Proc.Start(startArguments)
        result.ConsoleOut
        |> Seq.find find
        
    [<CustomOperation("filter")>]
    member this.Filter(opts, find: LineOut -> bool) =
        let startArguments = startArgs opts
        let result = Proc.Start(startArguments)
        result.ConsoleOut
        |> Seq.filter find
        |> List.ofSeq
        
    [<CustomOperation("output")>]
    member this.Output(opts) =
        let startArguments = startArgs opts
        Proc.Start(startArguments)
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, [<ParamArray>] arguments: string array) =
        let opts = { opts with Arguments = Some (arguments |> List.ofArray) }
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, arguments: string list) =
        let opts = { opts with Arguments = Some arguments}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments} |> acceptAnyExitCode
        let execArgs = execArgs opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)} |> acceptAnyExitCode
        let execArgs = execArgs opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("exit_code")>]
    member this.ReturnStatus(opts) =
        let execArgs = execArgs (acceptAnyExitCode opts)
        Proc.Exec(execArgs)
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        let execArgs = startArgs opts
        Proc.Start(execArgs)

    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments}
        Proc.Start(startArgs opts)

    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts) =
        let startArgs = startArgs opts
        Proc.Start(startArgs)
        
    [<CustomOperation("wait_until")>]
    member this.WaitUntil(opts, startedConfirmation: LineOut -> bool) =
        let opts = { opts with StartedConfirmationHandler = Some startedConfirmation }
        let longRunningArguments = longRunningArguments opts
        Proc.StartLongRunning(longRunningArguments, startedConfirmationTimeout opts)
        
    [<CustomOperation("wait_until_and_disconnect")>]
    member this.WaitUntilQuietAfter(opts, startedConfirmation: LineOut -> bool) =
        let opts =
            {
                opts with
                     StartedConfirmationHandler = Some startedConfirmation
                     StopBufferingAfterStarted = Some true 
            }
        let longRunningArguments = longRunningArguments opts
        Proc.StartLongRunning(longRunningArguments, startedConfirmationTimeout opts)
        
        

let exec = ExecBuilder()
    
    
