module Proc.Fs.Tests.ShellTests

open System
open System.IO
open ProcNet
open Proc.Fs
open Proc.Fs.Tests.TestBinary
open Xunit

let private bin = TestBinary.path
let private test args = cmd bin args |> Cmd.timeout (TimeSpan.FromSeconds 20.)

[<Fact>]
let ``let! binds captured output and return gives the result`` () =
    let result = shell {
        let! first = test [ "PrintArgs"; "one" ] |> Cmd.text
        let! rest = test [ "PrintArgs"; "two"; "three" ] |> Cmd.lines
        return first :: rest
    }
    Assert.Equal<string list>([ "one"; "two"; "three" ], result)

[<Fact>]
let ``the first failing command stops the block`` () =
    let ranAfter = ref false
    let ex = Assert.Throws<ProcExecException>(fun () ->
        shell {
            do! test [ "ExitWith"; "0" ]
            do! test [ "ExitWith"; "3" ]
            ranAfter.Value <- true
            do! test [ "ExitWith"; "0" ]
        })
    Assert.Equal(Nullable 3, ex.ExitCode)
    Assert.False(ranAfter.Value, "steps after the failing command ran")

[<Fact>]
let ``a failing capture stops the block`` () =
    let ranAfter = ref false
    Assert.Throws<ProcExecException>(fun () ->
        shell {
            let! _ = test [ "ExitWith"; "4" ] |> Cmd.lines
            ranAfter.Value <- true
        }) |> ignore
    Assert.False ranAfter.Value

[<Fact>]
let ``for loops over captured lines and runs a command for each`` () =
    let seen = ResizeArray<string>()
    shell {
        for arg in test [ "PrintArgs"; "a"; "b"; "c" ] |> Cmd.lines |> Cmd.where (fun l -> l <> "b") do
            let! echoed = test [ "PrintArgs"; arg + "!" ] |> Cmd.text
            seen.Add echoed
    }
    Assert.Equal<string list>([ "a!"; "c!" ], List.ofSeq seen)

[<Fact>]
let ``for loops over a plain sequence`` () =
    let codes = ResizeArray<int>()
    shell {
        for code in [ 1; 2 ] do
            let! c = test [ "ExitWith"; string code ] |> Cmd.exitCode
            codes.Add c
    }
    Assert.Equal<int list>([ 1; 2 ], List.ofSeq codes)

[<Fact>]
let ``if and match work inside the block`` () =
    let result = shell {
        let! code = test [ "ExitWith"; "2" ] |> Cmd.exitCode
        if code <> 0 then
            do! test [ "ExitWith"; "0" ]
        match code with
        | 2 -> return "two"
        | _ -> return "other"
    }
    Assert.Equal("two", result)

[<Fact>]
let ``try with catches a failing command`` () =
    let result = shell {
        try
            do! test [ "ExitWith"; "6" ]
            return 0
        with :? ProcExecException as e ->
            return e.ExitCode.Value
    }
    Assert.Equal(6, result)

[<Fact>]
let ``cd changes the working directory of the commands that follow`` () =
    use dir = new TempDir()
    let before, inside, here = shell {
        let! before = test [ "PrintCwd" ] |> Cmd.text
        do! cd dir.Path
        let! inside = test [ "PrintCwd" ] |> Cmd.text
        let! here = pwd
        return before, inside, here
    }
    Assert.NotEqual<string>(before, inside)
    // Compare the folder name only: macOS reports temp folders through a /private symlink
    Assert.EndsWith(Path.GetFileName dir.Path, inside)
    Assert.Equal(Path.GetFullPath dir.Path, here)

[<Fact>]
let ``a relative cd and a relative Cmd.cwd resolve against the current directory`` () =
    use dir = new TempDir()
    Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b")) |> ignore
    let inside = shell {
        do! cd dir.Path
        do! cd "a"
        let! inside = test [ "PrintCwd" ] |> Cmd.cwd "b" |> Cmd.text
        return inside
    }
    Assert.EndsWith(Path.Combine("a", "b"), inside)

[<Fact>]
let ``cd into a missing directory throws`` () =
    Assert.Throws<DirectoryNotFoundException>(fun () -> shell { do! cd "does-not-exist-proc-fs" }) |> ignore

[<Fact>]
let ``setEnv reaches every command and a command's own env wins`` () =
    let shared, own, piped = shell {
        do! setEnv "PROC_FS_A" "shared"
        do! setEnv "PROC_FS_B" "shared"
        let! shared = test [ "PrintEnv"; "PROC_FS_A" ] |> Cmd.text
        let! own = test [ "PrintEnv"; "PROC_FS_A"; "PROC_FS_B" ] |> Cmd.env [ "PROC_FS_B", "own" ] |> Cmd.lines
        let! piped = test [ "PrintEnv"; "PROC_FS_A" ] |>| test [ "EchoStdin"; "> " ] |> Cmd.text
        return shared, own, piped
    }
    Assert.Equal("shared", shared)
    Assert.Equal<string list>([ "shared"; "own" ], own)
    Assert.Equal("> shared", piped)

[<Fact>]
let ``the shell context does not leak into the next block`` () =
    shell { do! setEnv "PROC_FS_LEAK" "set" }
    let value = shell { return! test [ "PrintEnv"; "PROC_FS_LEAK" ] |> Cmd.text }
    Assert.Equal("<unset>", value)

[<Fact>]
let ``echoCommands prints each command before running it`` () =
    let original = Console.Out
    use writer = new StringWriter()
    Console.SetOut writer
    try
        shell {
            do! echoCommands true
            let! _ = test [ "PrintArgs"; "with space" ] |> Cmd.lines
            ()
        }
    finally Console.SetOut original
    Assert.Contains($"$ {bin} PrintArgs \"with space\"", writer.ToString())

[<Fact>]
let ``use disposes resources at the end of the block`` () =
    let disposed = ref false
    shell {
        use _ = { new IDisposable with member _.Dispose() = disposed.Value <- true }
        do! test [ "ExitWith"; "0" ]
    }
    Assert.True disposed.Value

[<Fact>]
let ``commands and steps on a line of their own run without do!`` () =
    use dir = new TempDir()
    let ranAfter = ref false
    let inside = shell {
        cd dir.Path
        sh $"{bin} ExitWith 0"
        test [ "ExitWith"; "0" ]
        let! inside = text $"{bin} PrintCwd"
        return inside
    }
    Assert.EndsWith(Path.GetFileName dir.Path, inside)
    let ex = Assert.Throws<ProcExecException>(fun () ->
        shell {
            sh $"{bin} ExitWith 3"
            ranAfter.Value <- true
        })
    Assert.Equal(Nullable 3, ex.ExitCode)
    Assert.False ranAfter.Value

[<Fact>]
let ``sh strings run pipes and keep values whole`` () =
    let spaced = "with space"
    let result = shell {
        let! piped = lines $"{bin} PrintArgs a {spaced} | {bin} EchoStdin '> '"
        return piped
    }
    Assert.Equal<string list>([ "> a"; "> with space" ], result)

[<Fact>]
let ``exitCode and succeeds return without throwing`` () =
    let code, ok, notOk = shell {
        let! code = exitCode $"{bin} ExitWith 4"
        let! ok = succeeds $"{bin} ExitWith 0"
        let! notOk = succeeds $"{bin} ExitWith 1"
        return code, ok, notOk
    }
    Assert.Equal(4, code)
    Assert.True ok
    Assert.False notOk

[<Fact>]
let ``shellx prints each command`` () =
    let original = Console.Out
    use writer = new StringWriter()
    Console.SetOut writer
    try shellx { sh $"{bin} ExitWith 0" }
    finally Console.SetOut original
    Assert.Contains($"$ {bin} ExitWith 0", writer.ToString())

[<Fact>]
let ``pushd and popd move through a stack of directories`` () =
    use dir = new TempDir()
    Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b")) |> ignore
    let seen = shell {
        cd dir.Path
        pushd "a"
        let! inA = text $"{bin} PrintCwd"
        pushd "b"
        let! inB = text $"{bin} PrintCwd"
        popd
        let! backInA = text $"{bin} PrintCwd"
        popd
        let! backHome = text $"{bin} PrintCwd"
        return [ inA; inB; backInA; backHome ]
    }
    let name = Path.GetFileName dir.Path
    Assert.Equal<string list>(
        [ Path.Combine(name, "a"); Path.Combine(name, "a", "b"); Path.Combine(name, "a"); name ],
        seen |> List.map (fun p -> p.Substring(p.IndexOf name)))

[<Fact>]
let ``popd without pushd throws`` () =
    Assert.Throws<InvalidOperationException>(fun () -> shell { popd }) |> ignore

[<Fact>]
let ``inDir restores the directory after an exception`` () =
    use dir = new TempDir()
    Directory.CreateDirectory(Path.Combine(dir.Path, "a")) |> ignore
    let after = shell {
        cd dir.Path
        try
            use! _ = inDir "a"
            sh $"{bin} ExitWith 1"
        with :? ProcExecException -> ()
        return! pwd
    }
    Assert.Equal(Path.GetFullPath dir.Path, after)

[<Fact>]
let ``inDir restores the directory at the end of its scope, even after a cd`` () =
    use dir = new TempDir()
    Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b")) |> ignore
    let insideScope, afterScope = shell {
        cd dir.Path
        let inside = ref ""
        for _ in [ 1 ] do
            use! _ = inDir "a"
            cd "b"
            let! here = pwd
            inside.Value <- here
        let! after = pwd
        return inside.Value, after
    }
    Assert.Equal(Path.Combine(Path.GetFullPath dir.Path, "a", "b"), insideScope)
    Assert.Equal(Path.GetFullPath dir.Path, afterScope)

[<Fact>]
let ``env, setEnvs and unsetEnv reach the child`` () =
    Environment.SetEnvironmentVariable("PROC_FS_PARENT", "from-parent")
    try
        let seen = shell {
            env "PROC_FS_A" "a"
            setEnvs [ "PROC_FS_B", "b"; "PROC_FS_C", "c" ]
            unsetEnv "PROC_FS_C"
            unsetEnv "PROC_FS_PARENT"
            return! lines $"{bin} PrintEnv PROC_FS_A PROC_FS_B PROC_FS_C PROC_FS_PARENT"
        }
        Assert.Equal<string list>([ "a"; "b"; "<unset>"; "<unset>" ], seen)
    finally Environment.SetEnvironmentVariable("PROC_FS_PARENT", null)

[<Fact>]
let ``getEnv reads the block's value, else the process's`` () =
    Environment.SetEnvironmentVariable("PROC_FS_PARENT", "from-parent")
    try
        let values = shell {
            let! parent = getEnv "PROC_FS_PARENT"
            env "PROC_FS_A" "a"
            let! own = getEnv "PROC_FS_A"
            unsetEnv "PROC_FS_PARENT"
            let! unset = getEnv "PROC_FS_PARENT"
            let! missing = getEnv "PROC_FS_NEVER_SET"
            return [ parent; own; unset; missing ]
        }
        Assert.Equal<string option list>([ Some "from-parent"; Some "a"; None; None ], values)
    finally Environment.SetEnvironmentVariable("PROC_FS_PARENT", null)

[<Fact>]
let ``withEnv restores previous values and absences at the end of its scope`` () =
    let inside, after = shell {
        env "PROC_FS_A" "outer"
        let inside = ref []
        for _ in [ 1 ] do
            use! _ = withEnv [ "PROC_FS_A", "inner"; "PROC_FS_B", "inner" ]
            let! seen = lines $"{bin} PrintEnv PROC_FS_A PROC_FS_B"
            inside.Value <- seen
        let! after = lines $"{bin} PrintEnv PROC_FS_A PROC_FS_B"
        return inside.Value, after
    }
    Assert.Equal<string list>([ "inner"; "inner" ], inside)
    Assert.Equal<string list>([ "outer"; "<unset>" ], after)
