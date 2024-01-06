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
    
    Timeout: TimeSpan option
    
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
            Timeout = None
            ValidExitCodeClassifier = None; 
            NoWrapInThread = None; SendControlCFirst = None; WaitForStreamReadersTimeout = None; 
        }

let private startArgs (opts: ExecOptions) =
    let startArguments = StartArguments(opts.Binary, opts.Arguments |> Option.defaultValue [])
    opts.LineOutFilter |> Option.iter(fun f -> startArguments.LineOutFilter <- f)
    opts.Environment |> Option.iter(fun e -> startArguments.Environment <- e)
    opts.WorkingDirectory |> Option.iter(fun d -> startArguments.WorkingDirectory <- d)
    opts.NoWrapInThread |> Option.iter(fun b -> startArguments.NoWrapInThread <- b)
    opts.SendControlCFirst |> Option.iter(fun b -> startArguments.SendControlCFirst <- b)
    opts.WaitForStreamReadersTimeout |> Option.iter(fun t -> startArguments.WaitForStreamReadersTimeout <- t)
    startArguments
 
let private execArgs (opts: ExecOptions) =
    let execArguments = ExecArguments(opts.Binary, opts.Arguments |> Option.defaultValue [])
    opts.Environment |> Option.iter(fun e -> execArguments.Environment <- e)
    opts.WorkingDirectory |> Option.iter(fun d -> execArguments.WorkingDirectory <- d)
    opts.ValidExitCodeClassifier |> Option.iter(fun f -> execArguments.ValidExitCodeClassifier <- f)
    execArguments
    

type ShellBuilder() =

    member t.Yield _ = ExecOptions.Empty
        
    [<CustomOperation("workingDirectory")>]
    member inline this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member inline this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    [<CustomOperation("timeout")>]
    member inline this.Timeout(opts, timeout) =
        { opts with Timeout = Some timeout }
        
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
        
    [<CustomOperation("binary")>]
    member this.Binary(opts, binary) =
        { opts with Binary = binary }
        
    [<CustomOperation("args")>]
    member inline this.Arguments(opts, [<ParamArray>] args: string array) =
        { opts with Arguments = Some (args |> List.ofArray) }
        
    [<CustomOperation("args")>]
    member inline this.Arguments(opts, args: string list) =
        { opts with Arguments = Some args}
        
    [<CustomOperation("workingDirectory")>]
    member inline this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member inline this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    [<CustomOperation("timeout")>]
    member inline this.Timeout(opts, timeout) =
        { opts with Timeout = Some timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member inline this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member inline this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<CustomOperation("thread_wrap")>]
    member inline this.NoWrapInThread(opts, threadWrap) =
        { opts with NoWrapInThread = Some (not threadWrap) }
        
    [<CustomOperation("filter_output")>]
    member inline this.FilterOutput(opts, find: LineOut -> bool) =
        { opts with LineOutFilter = Some find }
        
    [<CustomOperation("validExitCode")>]
    member inline this.ValidExitCode(opts, exitCodeClassifier: int -> bool) =
        { opts with ValidExitCodeClassifier = Some exitCodeClassifier }
    
    [<CustomOperation("find")>]
    member this.Find(opts, find: LineOut -> bool) =
        let opts = { opts with Find = Some find }
        let startArguments = startArgs opts
        let result = Proc.Start(startArguments)
        result.ConsoleOut
        |> Seq.find find
        
    [<CustomOperation("filter")>]
    member this.Filter(opts, find: LineOut -> bool) =
        let opts = { opts with Find = Some find }
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
    member this.InvokeArgs(opts, [<ParamArray>] args: string array) =
        let opts = { opts with Arguments = Some (args |> List.ofArray) }
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("run_args")>]
    member this.InvokeArgs(opts, args: string list) =
        let opts = { opts with Arguments = Some args}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("run")>]
    member this.Invoke(opts) =
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, args: string list) =
        let opts = { opts with Binary = binary; Arguments = Some args}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("run")>]
    member this.Execute(opts, binary, [<ParamArray>] args: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (args |> List.ofArray)}
        let execArgs = execArgs opts
        Proc.Exec(execArgs) |> ignore
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, args: string list) =
        let opts = { opts with Binary = binary; Arguments = Some args}
        let execArgs = execArgs opts
        Proc.Exec(execArgs).GetValueOrDefault 1
        
    [<CustomOperation("exit_code_of")>]
    member this.ReturnStatus(opts, binary, [<ParamArray>] args: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (args |> List.ofArray)}
        let execArgs = execArgs opts
        Proc.Exec(execArgs).GetValueOrDefault 1
        
    [<CustomOperation("exit_code")>]
    member this.ReturnStatus(opts) =
        let execArgs = execArgs opts
        Proc.Exec(execArgs).GetValueOrDefault 1
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts, binary, [<ParamArray>] args: string array) =
        let opts = { opts with Binary = binary; Arguments = Some (args |> List.ofArray)}
        let execArgs = startArgs opts
        Proc.Start(execArgs)
        
    [<CustomOperation("output_of")>]
    member this.ReturnOutput(opts) =
        let startArgs = startArgs opts
        Proc.Start(startArgs)
            

let exec = ExecBuilder()
    
    
