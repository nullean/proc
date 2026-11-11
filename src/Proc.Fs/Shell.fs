namespace Proc.Fs

open System
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open ProcNet
open ProcNet.Std

/// <summary>
/// A process that <c>background</c> started in a <c>shell { }</c> block. Disposing it stops the process: Ctrl+C
/// first, then a kill when it has not exited after 10 seconds. <c>use!</c> stops it at the end of the scope.
/// </summary>
[<Sealed>]
type Background internal (cmd: Cmd, subscription: LongRunningApplicationSubscription) =
    let mutable stopped = 0

    /// <summary>The command, with the directory and environment of the shell applied</summary>
    member _.Cmd = cmd
    /// <summary>The id of the process</summary>
    member _.ProcessId = Option.ofNullable subscription.ProcessId
    /// <summary>The exit code once the process exited on its own; <c>None</c> while it runs or after <c>Stop</c></summary>
    member _.ExitCode = Option.ofNullable subscription.ExitCode
    /// <summary>Whether the process exited on its own</summary>
    member b.HasExited = b.ExitCode.IsSome
    /// <summary>Sends Ctrl+C (SIGINT) to the process</summary>
    member _.SendControlC() = subscription.SendControlC()

    member internal _.IsStopped = stopped = 1
    member internal _.Subscription = subscription
    /// Marks the process as handled, so the shell no longer checks or stops it
    member internal _.Release() = Interlocked.Exchange(&stopped, 1) = 0

    /// <summary>Stops the process: Ctrl+C first, a kill after 10 seconds. Never throws.</summary>
    member b.Stop() =
        if b.Release() then
            try subscription.Dispose()
            with e -> Console.Error.WriteLine $"Stopping \"{Runner.describe cmd}\" failed: {e.Message}"

    interface IDisposable with
        member b.Dispose() = b.Stop()

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

    /// Cancelled when a sibling branch of <c>concurrently</c> fails
    member val internal Token = CancellationToken.None with get, set
    /// The context this one was forked from, whose background processes it also checks
    member val internal Parent: ShellContext option = None with get, set
    /// Background processes started in this context, oldest first
    member val internal Backgrounds = ResizeArray<Background>() with get

    /// A copy for one branch of <c>concurrently</c>: changes to it do not reach this context
    member internal ctx.Fork(token: CancellationToken) =
        ShellContext(
            WorkingDirectory = ctx.WorkingDirectory, Environment = ctx.Environment,
            DirectoryStack = ctx.DirectoryStack, EchoCommands = ctx.EchoCommands,
            Token = token, Parent = Some ctx)

    /// Throws when a background process of this context, or of one it was forked from, exited with an exit code
    /// its command does not accept
    member internal ctx.CheckBackgrounds() =
        for background in ctx.Backgrounds do
            match background.ExitCode with
            | Some code when not background.IsStopped ->
                let isValid = background.Cmd.ValidExitCodeClassifier |> Option.defaultValue (fun c -> c = 0)
                if not (isValid code) && background.Release() then
                    raise (ProcExecException(
                        $"Background process exited with '{code}' \"{Runner.describe background.Cmd}\"", ExitCode = Nullable code))
            | _ -> ()
        ctx.Parent |> Option.iter (fun parent -> parent.CheckBackgrounds())

    /// Stops the background processes this context started and nobody stopped, newest first
    member internal ctx.StopBackgrounds() =
        for i in ctx.Backgrounds.Count - 1 .. -1 .. 0 do ctx.Backgrounds[i].Stop()
        ctx.Backgrounds.Clear()

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
        ctx.Token.ThrowIfCancellationRequested()
        ctx.CheckBackgrounds()
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
    member _.Yield(cmd: Cmd) : ShellStep<unit> = ShellStep (fun ctx -> Runner.run ctx.Token (ctx.Prepare cmd))
    /// <summary>A step such as <c>cd "src"</c> on a line of its own</summary>
    member _.Yield(step: ShellStep<unit>) : ShellStep<unit> = step
    member _.Return(value: 'T) : ShellStep<'T> = ShellStep (fun _ -> value)
    member _.ReturnFrom(step: ShellStep<'T>) = step
    member _.ReturnFrom(capture: Capture<'T>) : ShellStep<'T> =
        ShellStep (fun ctx -> Runner.get ctx.Token { capture with Cmd = ctx.Prepare capture.Cmd })

    member _.Bind(step: ShellStep<'T>, f: 'T -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx -> ShellStep.run ctx step |> f |> ShellStep.run ctx)
    member _.Bind(cmd: Cmd, f: unit -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx ->
            Runner.run ctx.Token (ctx.Prepare cmd)
            ShellStep.run ctx (f ()))
    member _.Bind(capture: Capture<'T>, f: 'T -> ShellStep<'U>) : ShellStep<'U> =
        ShellStep (fun ctx ->
            Runner.get ctx.Token { capture with Cmd = ctx.Prepare capture.Cmd } |> f |> ShellStep.run ctx)

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
            for item in Runner.get ctx.Token { capture with Cmd = ctx.Prepare capture.Cmd } do ShellStep.run ctx (body item))
    member _.While(guard: unit -> bool, body: ShellStep<unit>) : ShellStep<unit> =
        ShellStep (fun ctx -> while guard () do ShellStep.run ctx body)

    member _.TryWith(body: ShellStep<'T>, handler: exn -> ShellStep<'T>) : ShellStep<'T> =
        ShellStep (fun ctx -> try ShellStep.run ctx body with e -> ShellStep.run ctx (handler e))
    member _.TryFinally(body: ShellStep<'T>, compensation: unit -> unit) : ShellStep<'T> =
        ShellStep (fun ctx -> try ShellStep.run ctx body finally compensation ())
    /// <summary>
    /// Disposes <paramref name="resource"/> at the end of the scope. When the scope failed and disposing fails too,
    /// the failure of the scope propagates and the failure to dispose goes to standard error.
    /// </summary>
    member _.Using(resource: 'R :> IDisposable, body: 'R -> ShellStep<'T>) : ShellStep<'T> =
        ShellStep (fun ctx ->
            let dispose () = if not (isNull (box resource)) then resource.Dispose()
            let result =
                try ShellStep.run ctx (body resource)
                with _ ->
                    try dispose ()
                    with cleanup -> Console.Error.WriteLine $"Cleanup failed after an earlier error: {cleanup.Message}"
                    reraise ()
            dispose ()
            result)

    /// <summary>Runs the block. Background processes still running at its end are stopped.</summary>
    member _.Run(step: ShellStep<'T>) : 'T =
        let ctx = ShellContext(EchoCommands = echoCommands)
        try ShellStep.run ctx step
        finally ctx.StopBackgrounds()

/// <summary>
/// Builds <c>steps { }</c> blocks: the same as <c>shell { }</c>, but the block does not run when it is evaluated.
/// It runs where it is used inside a <c>shell { }</c> block, in that block's directory and environment, or as one
/// branch of <c>concurrently</c>.
/// </summary>
type StepsBuilder() =
    inherit ShellBuilder(false)
    /// <summary>Returns the block as a step, without running it</summary>
    member _.Run(step: ShellStep<'T>) : ShellStep<'T> = step

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
    /// A block of steps that runs later, where it is used: <c>do! build</c> inside a <c>shell { }</c> block runs it
    /// in that block's directory and environment.
    /// <code>
    /// let build = steps { cd "src"; sh $"dotnet build" }
    /// shell { do! build }
    /// </code>
    /// </summary>
    let steps = StepsBuilder()

    let private runParallel (limit: int option) (branches: ShellStep<'T> list) : ShellStep<'T list> =
        ShellStep (fun ctx ->
            use cts = CancellationTokenSource.CreateLinkedTokenSource ctx.Token
            use gate = match limit with Some n -> new SemaphoreSlim(n, n) | None -> null
            let errorLock = obj ()
            let firstError: ExceptionDispatchInfo ref = ref null
            let fail (e: exn) =
                lock errorLock (fun () -> if isNull firstError.Value then firstError.Value <- ExceptionDispatchInfo.Capture e)
                cts.Cancel()
            let runBranch (branch: ShellStep<'T>) () : 'T option =
                let child = ctx.Fork cts.Token
                try
                    try
                        if not (isNull gate) then gate.Wait cts.Token
                        try Some (ShellStep.run child branch)
                        finally if not (isNull gate) then gate.Release() |> ignore
                    with
                    // A sibling failed, or the block was cancelled: not this branch's failure
                    | :? OperationCanceledException when cts.IsCancellationRequested -> None
                    | e -> fail e; None
                finally child.StopBackgrounds()
            // The branches block on processes, so each gets a thread of its own instead of one from the pool
            let tasks =
                branches |> List.map (fun branch ->
                    Task.Factory.StartNew(runBranch branch, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            Task.WaitAll(tasks |> List.map (fun t -> t :> Task) |> Array.ofList)
            if not (isNull firstError.Value) then firstError.Value.Throw()
            ctx.Token.ThrowIfCancellationRequested()
            tasks |> List.map (fun t -> t.Result.Value))

    /// <summary>
    /// Runs the branches at the same time and returns their results in the same order. Each branch starts with a
    /// copy of the directory and environment, so a <c>cd</c> in one does not reach the others. The first branch
    /// that fails stops the others, and its exception is the one that propagates.
    /// <code>
    /// let! results = concurrently [ steps { sh $"dotnet test a" }; steps { sh $"dotnet test b" } ]
    /// </code>
    /// </summary>
    let concurrently (branches: ShellStep<'T> list) : ShellStep<'T list> = runParallel None branches

    /// <summary>Like <c>concurrently</c>, but runs at most <paramref name="limit"/> branches at a time</summary>
    let concurrentlyAtMost (limit: int) (branches: ShellStep<'T> list) : ShellStep<'T list> =
        if limit < 1 then invalidArg (nameof limit) "concurrentlyAtMost needs a limit of at least 1"
        runParallel (Some limit) branches

    /// <summary>
    /// Starts <paramref name="cmd"/> in the background and goes on with the block: <c>use! api = background cmd</c>.
    /// With <c>Cmd.waitFor</c> it first waits for a line that says the process is ready; <c>Cmd.timeout</c> limits
    /// that wait (default 60 seconds).
    /// <para>
    /// When the process exits with an exit code it does not accept (<c>Cmd.validExitCode</c>, default only <c>0</c>),
    /// the next command of the block throws. <c>use!</c> stops the process at the end of the scope, and the block
    /// stops whatever is still running when it ends.
    /// </para>
    /// </summary>
    let background (cmd: Cmd) : ShellStep<Background> =
        ShellStep (fun ctx ->
            let cmd = ctx.Prepare cmd
            let cmd = { cmd with SendControlCFirst = Some (defaultArg cmd.SendControlCFirst true) }
            // StartLongRunning also returns when the process exits before it printed the line, so track it here
            let ready = ref false
            let isReady =
                cmd.StartedConfirmationHandler |> Option.map (fun isReady ->
                    fun (line: LineOut) -> if isReady line then ready.Value <- true; true else false)
            let args = Args.longRunning { cmd with StartedConfirmationHandler = isReady }
            if isReady.IsSome && cmd.StopBufferingAfterStarted.IsNone then args.StopBufferingAfterStarted <- true
            let startup = cmd.Timeout |> Option.defaultValue (TimeSpan.FromSeconds 60.)
            let subscription = Proc.StartLongRunning(args, startup, null, ctx.Token)
            let started = new Background(cmd, subscription)
            if isReady.IsSome && not ready.Value then
                started.Stop()
                let code = started.ExitCode |> Option.map string |> Option.defaultValue "?"
                raise (ProcExecException(
                    $"Background process exited with '{code}' before it was ready \"{Runner.describe cmd}\"",
                    ExitCode = Option.toNullable started.ExitCode))
            ctx.Backgrounds.Add started
            started)

    /// <summary>
    /// Waits until a <c>background</c> process exits, like <c>wait</c> in bash. Throws when its exit code is not
    /// accepted. The shell no longer checks or stops the process afterwards.
    /// </summary>
    let wait (background: Background) : ShellStep<unit> =
        ShellStep (fun ctx ->
            background.Subscription.WaitForCompletion(Nullable(), ctx.Token) |> ignore
            if background.Release() then
                let cmd = background.Cmd
                match background.ExitCode with
                | None -> raise (ProcExecException($"Process did not report an exit code \"{Runner.describe cmd}\""))
                | Some code ->
                    let isValid = cmd.ValidExitCodeClassifier |> Option.defaultValue (fun c -> c = 0)
                    if not (isValid code) then
                        raise (ProcExecException($"Process exited with '{code}' \"{Runner.describe cmd}\"", ExitCode = Nullable code))
            else invalidOp $"wait: \"{Runner.describe background.Cmd}\" was already stopped or waited for")

    let private currentDirectory (ctx: ShellContext) =
        ctx.WorkingDirectory |> Option.defaultWith Directory.GetCurrentDirectory

    let private changeDirectory (step: string) (ctx: ShellContext) (directory: string) =
        let next = Path.GetFullPath(Path.Combine(currentDirectory ctx, directory))
        if not (Directory.Exists next) then raise (DirectoryNotFoundException $"{step}: no such directory '{next}'")
        ctx.WorkingDirectory <- Some next

    let private restoring (restore: unit -> unit) =
        { new IDisposable with member _.Dispose() = restore () }

    /// <summary>
    /// Runs <paramref name="cmd"/> at the end of the enclosing scope, also when the scope fails, like
    /// <c>trap … EXIT</c>: <c>use! _ = defer (sh $"docker compose down")</c>. It runs in the directory and
    /// environment of the moment <c>defer</c> runs, so a later <c>cd</c> does not change it.
    /// </summary>
    let defer (cmd: Cmd) : ShellStep<IDisposable> =
        ShellStep (fun ctx ->
            let cmd = ctx.Apply cmd
            let echo = ctx.EchoCommands
            let ran = ref 0
            restoring (fun () ->
                if Interlocked.Exchange(&ran.contents, 1) = 0 then
                    if echo then Console.Out.WriteLine $"$ {Runner.describe cmd}"
                    // Not the block's token: clean-up runs even when the block was cancelled
                    Runner.run CancellationToken.None cmd))

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
