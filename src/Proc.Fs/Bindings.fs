module Proc.Fs

open System
open ProcNet
open ProcNet.Std

let execWithTimeout binary args timeout =
    let opts =
        ExecArguments(binary, args |> List.map (sprintf "\"%s\"") |> List.toArray)
    let options = args |> String.concat " "
    printfn ":: Running command: %s %s" binary options
    let r = Proc.Exec(opts, timeout)

    match r.HasValue with
    | true -> r.Value
    | false -> failwithf "invocation of `%s` timed out" binary

///executes
let exec2 (binary: string) (args: string list): int =
    execWithTimeout binary args (TimeSpan.FromMinutes 10)

let private redirected (binary: string) (args: string list) : ProcessCaptureResult = 
    Proc.Start(binary, args |> Array.ofList)
    
type RunningStatus = {
    LastExitCode: int
    GrepOutput: Std.LineOut list option
}
type ShellBuilder() =

    member t.Yield _ = None
        
    [<CustomOperation("exec")>]
    member inline this.ExecuteWithArguments(status, binary, [<ParamArray>] args: string array) =
        let exitCode = exec2 binary (args |> List.ofArray)
        match status with
        | None ->
            Some { LastExitCode =  exitCode; GrepOutput = None }
        | Some s ->
            Some { s with LastExitCode = exitCode }
            
    [<CustomOperation("grep")>]
    member this.Grep(status, searchForRe, binary, [<ParamArray>] args: string array) =
        let r = Proc.Start(binary, args)
        let o =
            r.ConsoleOut
            |> Seq.filter (_.Line.Contains(searchForRe))
            |> List.ofSeq
        
        match status with
        | None ->
            Some { LastExitCode =  0; GrepOutput = Some o }
        | Some s ->
            Some { LastExitCode =  0; GrepOutput = Some o }
        
let shell = ShellBuilder()

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

type ExecBuilder() =

    let startArgs (opts: ExecOptions) =
        let startArguments = StartArguments(opts.Binary, opts.Arguments |> Option.defaultValue [])
        opts.LineOutFilter |> Option.iter(fun f -> startArguments.LineOutFilter <- f)
        opts.Environment |> Option.iter(fun e -> startArguments.Environment <- e)
        opts.WorkingDirectory |> Option.iter(fun d -> startArguments.WorkingDirectory <- d)
        opts.NoWrapInThread |> Option.iter(fun b -> startArguments.NoWrapInThread <- b)
        opts.SendControlCFirst |> Option.iter(fun b -> startArguments.SendControlCFirst <- b)
        opts.WaitForStreamReadersTimeout |> Option.iter(fun t -> startArguments.WaitForStreamReadersTimeout <- t)
        startArguments
     
    let execArgs (opts: ExecOptions) =
        let execArguments = ExecArguments(opts.Binary, opts.Arguments |> Option.defaultValue [])
        opts.Environment |> Option.iter(fun e -> execArguments.Environment <- e)
        opts.WorkingDirectory |> Option.iter(fun d -> execArguments.WorkingDirectory <- d)
        opts.ValidExitCodeClassifier |> Option.iter(fun f -> execArguments.ValidExitCodeClassifier <- f)
        execArguments
    
    member t.Yield _ =
        {
            Binary = ""; Arguments = None; Find = None; 
            LineOutFilter = None; WorkingDirectory = None; Environment = None
            Timeout = None
            ValidExitCodeClassifier = None; 
            NoWrapInThread = None; SendControlCFirst = None; WaitForStreamReadersTimeout = None; 
        }
        
    [<CustomOperation("binary")>]
    member inline this.Binary(opts, binary) =
        { opts with Binary = binary }
            
    [<CustomOperation("args")>]
    member inline this.Arguments(opts, [<ParamArray>] args: string array) =
        { opts with Arguments = Some (args |> List.ofArray) }
        
    [<CustomOperation("args")>]
    member inline this.Arguments(opts, args: string list) =
        { opts with Arguments = Some args}
        
    [<CustomOperation("workingDirectory")>]
    member this.WorkingDirectory(opts, workingDirectory: string) =
        { opts with WorkingDirectory = Some workingDirectory }
        
    [<CustomOperation("env")>]
    member this.EnvironmentVariables(opts, env: Map<string, string>) =
        { opts with Environment = Some env }
        
    [<CustomOperation("timeout")>]
    member this.Timeout(opts, timeout) =
        { opts with Timeout = Some timeout }
        
    [<CustomOperation("stream_reader_wait_timeout")>]
    member this.WaitForStreamReadersTimeout(opts, timeout) =
        { opts with WaitForStreamReadersTimeout = Some timeout }
        
    [<CustomOperation("send_control_c")>]
    member this.SendControlCFirst(opts, sendControlCFirst) =
        { opts with SendControlCFirst = Some sendControlCFirst }
        
    [<CustomOperation("thread_wrap")>]
    member this.NoWrapInThread(opts, threadWrap) =
        { opts with NoWrapInThread = Some (not threadWrap) }
        
    [<CustomOperation("filterOutput")>]
    member this.FilterOutput(opts, find: LineOut -> bool) =
        { opts with LineOutFilter = Some find }
        
    [<CustomOperation("validExitCode")>]
    member this.ValidExitCode(opts, exitCodeClassifier: int -> bool) =
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
        
    [<CustomOperation("invoke_args")>]
    member inline this.Invoke(opts, [<ParamArray>] args: string array) =
        let opts = { opts with Arguments = Some (args |> List.ofArray) }
        let execArgs = execArgs opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("invoke_args")>]
    member inline this.Invoke(opts, args: string list) =
        let opts = { opts with Arguments = Some args}
        let execArgs = execArgs opts
        Proc.Exec(execArgs)
        
    [<CustomOperation("invoke")>]
    member inline this.Invoke(opts) =
        let execArgs = execArgs opts
        Proc.Exec(execArgs)

let exec = ExecBuilder()
    
    
