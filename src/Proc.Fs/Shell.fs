namespace Proc.Fs

open System
open System.IO

/// <summary>
/// The working directory, environment and echo setting shared by every command in one <c>shell { }</c> block.
/// Steps such as <c>cd</c> and <c>setEnv</c> change it for the commands that follow.
/// </summary>
type ShellContext() =
    member val WorkingDirectory: string option = None with get, set
    member val Environment: Map<string, string> = Map.empty with get, set
    /// <summary>Print <c>$ binary arguments</c> before running each command, like <c>set -x</c></summary>
    member val EchoCommands = false with get, set

    /// Fills in what the command does not set itself: a relative working directory resolves against the shell's,
    /// and the command's own environment variables win over the shell's
    member internal ctx.Apply(cmd: Cmd) : Cmd =
        let workingDirectory =
            match cmd.WorkingDirectory, ctx.WorkingDirectory with
            | Some own, Some shared -> Some (Path.Combine(shared, own))
            | Some own, None -> Some own
            | None, shared -> shared
        let environment =
            match cmd.Environment with
            | Some own -> Some (own |> Map.fold (fun m k v -> Map.add k v m) ctx.Environment)
            | None when Map.isEmpty ctx.Environment -> None
            | None -> Some ctx.Environment
        let stdin =
            match cmd.Stdin with
            | Some (StdinSource.Upstream upstream) -> Some (StdinSource.Upstream (ctx.Apply upstream))
            | other -> other
        { cmd with WorkingDirectory = workingDirectory; Environment = environment; Stdin = stdin }

    member internal ctx.Prepare(cmd: Cmd) =
        let cmd = ctx.Apply cmd
        if ctx.EchoCommands then Console.Out.WriteLine $"$ {Runner.describe cmd}"
        cmd

/// <summary>One step of a <c>shell { }</c> block, run against the block's <see cref="ShellContext"/></summary>
type ShellStep<'T> = internal ShellStep of (ShellContext -> 'T)

module internal ShellStep =
    let run (ctx: ShellContext) (ShellStep step) = step ctx

/// <summary>
/// Builds <c>shell { }</c> blocks. <c>do!</c> runs a <see cref="Cmd"/>, <c>let!</c> runs a <see cref="Capture{T}"/>
/// and binds what it reads. The first failing command throws <see cref="ProcNet.ProcExecException"/> and stops the
/// block. <c>for</c>, <c>while</c>, <c>if</c>, <c>match</c>, <c>try</c> and <c>use</c> work as usual.
/// The block runs as soon as it is evaluated and returns the value of its <c>return</c>.
/// <para>A command or a step such as <c>cd "src"</c> on a line of its own runs without <c>do!</c>.</para>
/// </summary>
type ShellBuilder(echoCommands: bool) =
    new() = ShellBuilder(false)

    member _.Zero() : ShellStep<unit> = ShellStep ignore
    /// <summary>A command on a line of its own: runs it and stops the block when it fails</summary>
    member _.Yield(cmd: Cmd) : ShellStep<unit> = ShellStep (fun ctx -> Runner.run (ctx.Prepare cmd))
    /// <summary>A step such as <c>cd "src"</c> on a line of its own</summary>
    member _.Yield(step: ShellStep<unit>) : ShellStep<unit> = step
    member _.Return(value: 'T) : ShellStep<'T> = ShellStep (fun _ -> value)
    member _.ReturnFrom(step: ShellStep<'T>) = step
    member _.ReturnFrom(capture: Capture<'T>) : ShellStep<'T> =
        ShellStep (fun ctx -> Runner.get { capture with Cmd = ctx.Prepare capture.Cmd })

    member _.Bind(step: ShellStep<'T>, f: 'T -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx -> ShellStep.run ctx step |> f |> ShellStep.run ctx)
    member _.Bind(cmd: Cmd, f: unit -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx ->
            Runner.run (ctx.Prepare cmd)
            ShellStep.run ctx (f ()))
    member _.Bind(capture: Capture<'T>, f: 'T -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx ->
            Runner.get { capture with Cmd = ctx.Prepare capture.Cmd } |> f |> ShellStep.run ctx)

    member _.Delay(f: unit -> ShellStep<'T>) : ShellStep<'T> = ShellStep (fun ctx -> ShellStep.run ctx (f ()))
    member _.Combine(first: ShellStep<unit>, second: ShellStep<'T>) : ShellStep<'T> =
        ShellStep (fun ctx ->
            ShellStep.run ctx first
            ShellStep.run ctx second)

    member _.For(items: seq<'T>, body: 'T -> ShellStep<unit>) : ShellStep<unit> =
        ShellStep (fun ctx -> for item in items do ShellStep.run ctx (body item))
    /// <summary><c>for line in cmd "git" ["ls-files"] |> Cmd.lines do</c> runs the command, then loops</summary>
    member _.For(capture: Capture<'T list>, body: 'T -> ShellStep<unit>) : ShellStep<unit> =
        ShellStep (fun ctx ->
            for item in Runner.get { capture with Cmd = ctx.Prepare capture.Cmd } do ShellStep.run ctx (body item))
    member _.While(guard: unit -> bool, body: ShellStep<unit>) : ShellStep<unit> =
        ShellStep (fun ctx -> while guard () do ShellStep.run ctx body)

    member _.TryWith(body: ShellStep<'T>, handler: exn -> ShellStep<'T>) : ShellStep<'T> =
        ShellStep (fun ctx -> try ShellStep.run ctx body with e -> ShellStep.run ctx (handler e))
    member _.TryFinally(body: ShellStep<'T>, compensation: unit -> unit) : ShellStep<'T> =
        ShellStep (fun ctx -> try ShellStep.run ctx body finally compensation ())
    member _.Using(resource: 'R :> IDisposable, body: 'R -> ShellStep<'T>) : ShellStep<'T> =
        ShellStep (fun ctx ->
            try ShellStep.run ctx (body resource)
            finally if not (isNull (box resource)) then resource.Dispose())

    member _.Run(step: ShellStep<'T>) : 'T = ShellStep.run (ShellContext(EchoCommands = echoCommands)) step

/// <summary>Brought into scope by <c>open Proc.Fs</c></summary>
[<AutoOpen>]
module ShellSteps =

    /// <summary>
    /// Runs a sequence of commands, stopping at the first failure.
    /// <code>
    /// let sha = shell {
    ///     do! cmd "git" ["fetch"]
    ///     let! sha = cmd "git" ["rev-parse"; "HEAD"] |> Cmd.text
    ///     return sha
    /// }
    /// </code>
    /// </summary>
    let shell = ShellBuilder()

    /// <summary>Like <c>shell</c>, but prints <c>$ binary arguments</c> before each command, like <c>set -x</c></summary>
    let shellx = ShellBuilder(true)

    /// <summary>
    /// Changes the working directory for the commands that follow in this <c>shell { }</c> block.
    /// A relative path resolves against the current one. Throws when the directory does not exist.
    /// </summary>
    let cd (directory: string) : ShellStep<unit> =
        ShellStep (fun ctx ->
            let current = ctx.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory
            let next = Path.GetFullPath(Path.Combine(current, directory))
            if not (Directory.Exists next) then raise (DirectoryNotFoundException $"cd: no such directory '{next}'")
            ctx.WorkingDirectory <- Some next)

    /// <summary>Sets an environment variable for the commands that follow in this <c>shell { }</c> block</summary>
    let setEnv (name: string) (value: string) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.Environment <- Map.add name value ctx.Environment)

    /// <summary>Print <c>$ binary arguments</c> before each command that follows, like <c>set -x</c></summary>
    let echoCommands (enabled: bool) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.EchoCommands <- enabled)

    /// <summary>The working directory commands in this <c>shell { }</c> block run in</summary>
    let pwd : ShellStep<string> =
        ShellStep (fun ctx -> ctx.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory)
