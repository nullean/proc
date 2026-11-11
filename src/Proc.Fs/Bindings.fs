/// <summary>The <c>exec { }</c> builder. Brought into scope by <c>open Proc.Fs</c></summary>
[<AutoOpen>]
module Proc.Fs.Bindings

open System
open ProcNet
open ProcNet.Std

type ExecBuilder() =

    member t.Yield _ = ExecOptions.Empty
        
    ///<summary>Runs <paramref name="binary"/> using <paramref name="arguments"/> immediately</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    /// <param name="arguments">the arguments to pass on to the binary being executed</param>
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, [<ParamArray>] arguments: string array) =
        Runner.run { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        
    ///<summary>Runs <paramref name="binary"/> using <paramref name="arguments"/> immediately</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    /// <param name="arguments">the arguments to pass on to the binary being executed</param>
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, arguments: string list) =
        Runner.run { opts with Binary = binary; Arguments = Some arguments}
        
    ///<summary>
    /// Runs the <see cref="ExecOptions"/> the computation build thus far.
    /// <para>Needs at least `binary` to be specified</para>
    /// </summary>
    [<CustomOperation("run")>]
    member this.Execute(opts) =
        if opts.Binary = "" then failwithf "No binary specified to exec computation expression"
        Runner.run opts
        
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

    ///<summary>Accepts any exit code, like <c>|| true</c>. A timeout still throws.</summary>
    [<CustomOperation("allow_failure")>]
    member this.AllowFailure(opts: ExecOptions) = Cmd.allowFailure opts

    ///<summary>Hides what the process prints unless it fails. Only <c>run</c> supports it.</summary>
    [<CustomOperation("quiet")>]
    member this.Quiet(opts: ExecOptions) = Cmd.quiet opts

    ///<summary>Runs the process up to <paramref name="retries"/> more times while it fails. Only <c>run</c> supports it.</summary>
    [<CustomOperation("retry")>]
    member this.Retry(opts: ExecOptions, retries: int) = Cmd.retry retries opts

    ///<summary>Waits <paramref name="delay"/> before each new attempt of <c>retry</c></summary>
    [<CustomOperation("retry_delay")>]
    member this.RetryDelay(opts: ExecOptions, delay: TimeSpan) = Cmd.retryDelay delay opts

    ///<summary>Writes standard out to a file, replacing it, like <c>&gt; file</c>. Only <c>run</c> supports it.</summary>
    [<CustomOperation("to_file")>]
    member this.ToFile(opts: ExecOptions, path: string) = Cmd.toFile path opts

    ///<summary>Adds standard out to the end of a file, like <c>&gt;&gt; file</c>. Only <c>run</c> supports it.</summary>
    [<CustomOperation("append_to_file")>]
    member this.AppendToFile(opts: ExecOptions, path: string) = Cmd.appendToFile path opts

    ///<summary>Reads standard input from a file, like <c>&lt; file</c>. Only <c>run</c> supports it.</summary>
    [<CustomOperation("stdin_file")>]
    member this.StdinFile(opts: ExecOptions, path: string) = Cmd.stdinFile path opts

    ///<summary>Writes these lines to standard input, then closes it. Only <c>run</c> supports it.</summary>
    [<CustomOperation("stdin")>]
    member this.Stdin(opts: ExecOptions, lines: string list) = Cmd.stdin lines opts

    [<CustomOperation("find")>]
    member this.Find(opts, find: LineOut -> bool) =
        let startArguments = Args.start opts
        let result = Proc.Start(startArguments)
        result.ConsoleOut
        |> Seq.find find
        
    [<CustomOperation("filter")>]
    member this.Filter(opts, find: LineOut -> bool) =
        let startArguments = Args.start opts
        let result = Proc.Start(startArguments)
        result.ConsoleOut
        |> Seq.filter find
        |> List.ofSeq
        
    [<CustomOperation("output")>]
    member this.Output(opts) =
        let startArguments = Args.start opts
        Proc.Start(startArguments)
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, [<ParamArray>] arguments: string array) =
        Runner.run { opts with Arguments = Some (arguments |> List.ofArray) }
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, arguments: string list) =
        Runner.run { opts with Arguments = Some arguments }
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments} |> Args.acceptAnyExitCode
        let execArgs = Args.exec opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)} |> Args.acceptAnyExitCode
        let execArgs = Args.exec opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("exit_code")>]
    member this.ReturnStatus(opts) =
        let execArgs = Args.exec (Args.acceptAnyExitCode opts)
        Proc.Exec(execArgs)
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        let execArgs = Args.start opts
        Proc.Start(execArgs)

    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments}
        Proc.Start(Args.start opts)

    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts) =
        let startArgs = Args.start opts
        Proc.Start(startArgs)
        
    [<CustomOperation("wait_until")>]
    member this.WaitUntil(opts, startedConfirmation: LineOut -> bool) =
        let opts = { opts with StartedConfirmationHandler = Some startedConfirmation }
        let longRunningArguments = Args.longRunning opts
        Proc.StartLongRunning(longRunningArguments, Args.startedConfirmationTimeout opts)
        
    [<CustomOperation("wait_until_and_disconnect")>]
    member this.WaitUntilQuietAfter(opts, startedConfirmation: LineOut -> bool) =
        let opts =
            {
                opts with
                     StartedConfirmationHandler = Some startedConfirmation
                     StopBufferingAfterStarted = Some true 
            }
        let longRunningArguments = Args.longRunning opts
        Proc.StartLongRunning(longRunningArguments, Args.startedConfirmationTimeout opts)
        
        

let exec = ExecBuilder()
    
    
