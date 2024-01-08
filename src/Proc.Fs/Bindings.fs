module Proc.Fs

open System
open ProcNet
open ProcNet.Std

type ExecOptions = {
    Binary: string
    Arguments: string list option
    LineOutFilter: (LineOut -> bool) option
    Find: (LineOut -> bool) option
    WorkingDirectory: string option
    Environment: Map<string, string> option
    
    Timeout: TimeSpan 
    ValidExitCodeClassifier: (int -> bool) option
    
    NoWrapInThread: bool option
    SendControlCFirst: bool option
    WaitForStreamReadersTimeout: TimeSpan option
}
with
    static member Empty = 
        {
            Binary = ""; Arguments = None; Find = None; 
            LineOutFilter = None; WorkingDirectory = None; Environment = None
            Timeout = TimeSpan(0, 0, 0, 0, -1)
            ValidExitCodeClassifier = None; 
            NoWrapInThread = None; SendControlCFirst = None; WaitForStreamReadersTimeout = None; 
        }

let private startArgs (opts: ExecOptions) =
    let args = opts.Arguments |> Option.defaultValue []
    let startArguments = StartArguments(opts.Binary, args)
    opts.LineOutFilter |> Option.iter(fun f -> startArguments.LineOutFilter <- f)
    opts.Environment |> Option.iter(fun e -> startArguments.Environment <- e)
    opts.WorkingDirectory |> Option.iter(fun d -> startArguments.WorkingDirectory <- d)
    opts.NoWrapInThread |> Option.iter(fun b -> startArguments.NoWrapInThread <- b)
    opts.SendControlCFirst |> Option.iter(fun b -> startArguments.SendControlCFirst <- b)
    opts.WaitForStreamReadersTimeout |> Option.iter(fun t -> startArguments.WaitForStreamReadersTimeout <- t)
    startArguments
 
let private execArgs (opts: ExecOptions) =
    let args = opts.Arguments |> Option.defaultValue []
    let execArguments = ExecArguments(opts.Binary, args)
    opts.Environment |> Option.iter(fun e -> execArguments.Environment <- e)
    opts.WorkingDirectory |> Option.iter(fun d -> execArguments.WorkingDirectory <- d)
    opts.ValidExitCodeClassifier |> Option.iter(fun f -> execArguments.ValidExitCodeClassifier <- f)
    execArguments
    

type ShellBuilder() =

    member t.Yield _ = ExecOptions.Empty
        
    [<CustomOperation("working_dir")>]
    member inline this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member inline this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    [<CustomOperation("timeout")>]
    member inline this.Timeout(opts, timeout) =
        { opts with Timeout = timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member inline this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member inline this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<CustomOperation("thread_wrap")>]
    member inline this.NoWrapInThread(opts, threadWrap) =
        { opts with NoWrapInThread = Some (not threadWrap) }
        
    [<CustomOperation("exec")>]
    member this.ExecuteWithArguments(opts, binary, [<ParamArray>] args: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (args |> List.ofArray)  }
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        opts
        
    [<CustomOperation("exec")>]
    member this.ExecuteWithArguments(opts, binary, args: string list) =
        let opts = { opts with Binary = binary; Arguments = Some args }
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
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
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        
    ///<summary>Runs <paramref name="binary"/> using <paramref name="arguments"/> immediately</summary>
    /// <param name="opts"><see cref="ExecOptions"/> the computation build thus far, not specified directly</param>
    /// <param name="binary">The binary to execute</param>
    /// <param name="arguments">the arguments to pass on to the binary being executed</param>
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments}
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        
    ///<summary>
    /// Runs the <see cref="ExecOptions"/> the computation build thus far.
    /// <para>Needs at least `binary` to be specified</para>
    /// </summary>
    [<CustomOperation("run")>]
    member this.Execute(opts) =
        if opts.Binary = "" then failwithf "No binary specified to exec computation expression"
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        
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
        
    [<CustomOperation("timeout")>]
    member this.Timeout(opts, timeout) =
        { opts with Timeout = timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<CustomOperation("thread_wrap")>]
    member this.NoWrapInThread(opts, threadWrap) =
        { opts with NoWrapInThread = Some (not threadWrap) }
        
    [<CustomOperation("filter_output")>]
    member this.FilterOutput(opts, find: LineOut -> bool) =
        { opts with LineOutFilter = Some find }
        
    [<CustomOperation("validExitCode")>]
    member this.ValidExitCode(opts, exitCodeClassifier: int -> bool) =
        { opts with ValidExitCodeClassifier = Some exitCodeClassifier }
    
    [<CustomOperation("find")>]
    member this.Find(opts, find: LineOut -> bool) =
        let opts = { opts with Find = Some find }
        let startArguments = startArgs opts
        let result = Proc.Start(startArguments, opts.Timeout)
        result.ConsoleOut
        |> Seq.find find
        
    [<CustomOperation("filter")>]
    member this.Filter(opts, find: LineOut -> bool) =
        let opts = { opts with Find = Some find }
        let startArguments = startArgs opts
        let result = Proc.Start(startArguments, opts.Timeout)
        result.ConsoleOut
        |> Seq.filter find
        |> List.ofSeq
        
    [<CustomOperation("output")>]
    member this.Output(opts) =
        let startArguments = startArgs opts
        Proc.Start(startArguments, opts.Timeout)
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, [<ParamArray>] arguments: string array) =
        let opts = { opts with Arguments = Some (arguments |> List.ofArray) }
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, arguments: string list) =
        let opts = { opts with Arguments = Some arguments}
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout) |> ignore
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, arguments: string list) =
        let opts = { opts with Binary = binary; Arguments = Some arguments}
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout).GetValueOrDefault 1
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout).GetValueOrDefault 1
        
    [<CustomOperation("exit_code")>]
    member this.ReturnStatus(opts) =
        let execArgs = execArgs opts
        Proc.Exec(execArgs, opts.Timeout).GetValueOrDefault 1
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, [<ParamArray>] arguments: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (arguments |> List.ofArray)}
        let execArgs = startArgs opts
        Proc.Start(execArgs, opts.Timeout)
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts) =
        let startArgs = startArgs opts
        Proc.Start(startArgs, opts.Timeout)
        

let exec = ExecBuilder()
    
    
