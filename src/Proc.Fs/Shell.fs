namespace Proc.Fs

open System
open System.IO

/// <summary>
/// The working directory, environment and echo setting shared by every command in one <c>shell { }</c> block.
/// Steps such as <c>cd</c> and <c>setEnv</c> change it for the commands that follow.
/// </summary>
type ShellContext() =
    member val WorkingDirectory: string option = None with get, set
    /// <summary>Variables set for the commands of this block. A <c>null</c> value means unset.</summary>
    member val Environment: Map<string, string> = Map.empty with get, set
    /// <summary>Directories <c>pushd</c> saved, most recent first</summary>
    member val DirectoryStack: string list = [] with get, set
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

    let private currentDirectory (ctx: ShellContext) =
        ctx.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory

    let private changeDirectory (step: string) (ctx: ShellContext) (directory: string) =
        let next = Path.GetFullPath(Path.Combine(currentDirectory ctx, directory))
        if not (Directory.Exists next) then raise (DirectoryNotFoundException $"{step}: no such directory '{next}'")
        ctx.WorkingDirectory <- Some next

    let private restoring (restore: unit -> unit) =
        { new IDisposable with member _.Dispose() = restore () }

    /// <summary>
    /// Changes the working directory for the commands that follow in this <c>shell { }</c> block.
    /// A relative path resolves against the current one. Throws when the directory does not exist.
    /// </summary>
    let cd (directory: string) : ShellStep<unit> = ShellStep (fun ctx -> changeDirectory "cd" ctx directory)

    /// <summary>Saves the current directory, then changes to <paramref name="directory"/>. <c>popd</c> goes back.</summary>
    let pushd (directory: string) : ShellStep<unit> =
        ShellStep (fun ctx ->
            let current = currentDirectory ctx
            changeDirectory "pushd" ctx directory
            ctx.DirectoryStack <- current :: ctx.DirectoryStack)

    /// <summary>Goes back to the directory the last <c>pushd</c> saved. Throws when there is none.</summary>
    let popd : ShellStep<unit> =
        ShellStep (fun ctx ->
            match ctx.DirectoryStack with
            | previous :: rest ->
                ctx.WorkingDirectory <- Some previous
                ctx.DirectoryStack <- rest
            | [] -> invalidOp "popd: the directory stack is empty")

    /// <summary>
    /// Changes to <paramref name="directory"/> until the end of the enclosing scope:
    /// <c>use! _ = inDir "src"</c>. The previous directory comes back even after a <c>cd</c> or an exception.
    /// </summary>
    let inDir (directory: string) : ShellStep<IDisposable> =
        ShellStep (fun ctx ->
            let saved = ctx.WorkingDirectory
            changeDirectory "inDir" ctx directory
            restoring (fun () -> ctx.WorkingDirectory <- saved))

    /// <summary>Sets an environment variable for the commands that follow in this <c>shell { }</c> block</summary>
    let setEnv (name: string) (value: string) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.Environment <- Map.add name value ctx.Environment)

    /// <summary>Sets an environment variable for the commands that follow, like <c>export NAME=value</c></summary>
    let env (name: string) (value: string) : ShellStep<unit> = setEnv name value

    /// <summary>Sets several environment variables for the commands that follow</summary>
    let setEnvs (variables: (string * string) list) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.Environment <- variables |> List.fold (fun m (k, v) -> Map.add k v m) ctx.Environment)

    /// <summary>Removes an environment variable for the commands that follow, even one this process has</summary>
    let unsetEnv (name: string) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.Environment <- Map.add name null ctx.Environment)

    /// <summary>
    /// The value commands in this block see for <paramref name="name"/>: the block's own value, else this process's.
    /// <c>None</c> when it is not set or was unset.
    /// </summary>
    let getEnv (name: string) : ShellStep<string option> =
        ShellStep (fun ctx ->
            match ctx.Environment.TryFind name with
            | Some value -> Option.ofObj value
            | None -> Option.ofObj (Environment.GetEnvironmentVariable name))

    /// <summary>
    /// Sets environment variables until the end of the enclosing scope: <c>use! _ = withEnv ["CI", "true"]</c>.
    /// The previous values, or their absence, come back afterwards.
    /// </summary>
    let withEnv (variables: (string * string) list) : ShellStep<IDisposable> =
        ShellStep (fun ctx ->
            let saved = [ for name, _ in variables -> name, ctx.Environment.TryFind name ]
            ctx.Environment <- variables |> List.fold (fun m (k, v) -> Map.add k v m) ctx.Environment
            restoring (fun () ->
                ctx.Environment <-
                    saved |> List.fold (fun m (name, previous) ->
                        match previous with
                        | Some value -> Map.add name value m
                        | None -> Map.remove name m) ctx.Environment))

    let private blockVariable (ctx: ShellContext) (name: string) =
        match ctx.Environment.TryFind name with
        | Some value -> Option.ofObj value
        | None -> Option.ofObj (Environment.GetEnvironmentVariable name)

    /// <summary>
    /// The full path of the program <paramref name="name"/> on the PATH commands in this block see, or <c>None</c>:
    /// <c>let! docker = which "docker"</c>. A name with a directory in it resolves against the current directory.
    /// </summary>
    let which (name: string) : ShellStep<string option> =
        ShellStep (fun ctx -> Which.find (currentDirectory ctx) (blockVariable ctx "PATH") (blockVariable ctx "PATHEXT") name)

    /// <summary>
    /// Stops the block with a <see cref="FileNotFoundException"/> when the program <paramref name="name"/> is not on
    /// the PATH, before any later command runs: <c>require "git"</c> on a line of its own.
    /// </summary>
    let require (name: string) : ShellStep<unit> =
        ShellStep (fun ctx ->
            let path = blockVariable ctx "PATH"
            match Which.find (currentDirectory ctx) path (blockVariable ctx "PATHEXT") name with
            | Some _ -> ()
            | None -> raise (FileNotFoundException($"require: '{name}' is not on the PATH", name)))

    /// <summary>Print <c>$ binary arguments</c> before each command that follows, like <c>set -x</c></summary>
    let echoCommands (enabled: bool) : ShellStep<unit> =
        ShellStep (fun ctx -> ctx.EchoCommands <- enabled)

    /// <summary>The working directory commands in this <c>shell { }</c> block run in</summary>
    let pwd : ShellStep<string> =
        ShellStep (fun ctx -> ctx.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory)
