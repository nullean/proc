/// Files, quiet, retry, allowFailure and which: in Cmd, sh strings, exec { } and shell { }
module Proc.Fs.Tests.RedirectRetryTests

open System
open System.IO
open ProcNet
open Proc.Fs
open Proc.Fs.Tests.TestBinary
open TUnit.Core

let private bin = TestBinary.path
let private test args = cmd bin args |> Cmd.timeout (TimeSpan.FromSeconds 20.)
let private eq (expected: 'T) (actual: 'T) = Assert.Equal<'T>(expected, actual)
let private readLines path = File.ReadAllLines path |> List.ofArray

/// Runs f with standard out and standard error captured
let private captureConsole (f: unit -> unit) =
    let out, err = Console.Out, Console.Error
    use outWriter = new StringWriter()
    use errWriter = new StringWriter()
    Console.SetOut outWriter
    Console.SetError errWriter
    try
        try f () with _ -> ()
    finally
        Console.SetOut out
        Console.SetError err
    outWriter.ToString(), errWriter.ToString()

let private makeTool (dir: TempDir) name =
    let file = dir.File(if OperatingSystem.IsWindows() then name + ".exe" else name)
    File.WriteAllText(file, "")
    if not (OperatingSystem.IsWindows()) then
        File.SetUnixFileMode(file, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    file

type RedirectRetryTests() =

    // ---- files ----

    [<Test>]
    member _.``toFile writes standard out to the file and replaces what was there`` () =
        use dir = new TempDir()
        let file = dir.File "out.txt"
        File.WriteAllText(file, "old\n")
        test [ "PrintArgs"; "a"; "with space" ] |> Cmd.toFile file |> Cmd.run
        eq [ "a"; "with space" ] (readLines file)

    [<Test>]
    member _.``toFile keeps standard error on the console`` () =
        use dir = new TempDir()
        let file = dir.File "out.txt"
        let out, err = captureConsole (fun () ->
            test [ "FailTimes"; dir.File "count"; "1" ] |> Cmd.allowFailure |> Cmd.toFile file |> Cmd.run)
        eq [ "attempt 1" ] (readLines file)
        Assert.DoesNotContain("attempt", out)
        Assert.Contains("failed 1", err)

    [<Test>]
    member _.``appendToFile adds to the end of the file`` () =
        use dir = new TempDir()
        let file = dir.File "out.txt"
        File.WriteAllLines(file, [ "old" ])
        test [ "PrintArgs"; "new" ] |> Cmd.appendToFile file |> Cmd.run
        eq [ "old"; "new" ] (readLines file)

    [<Test>]
    member _.``a relative file resolves against the working directory of the command`` () =
        use dir = new TempDir()
        File.WriteAllLines(dir.File "in.txt", [ "x" ])
        test [ "EchoStdin"; "> " ] |> Cmd.cwd dir.Path |> Cmd.stdinFile "in.txt" |> Cmd.toFile "out.txt" |> Cmd.run
        eq [ "> x" ] (readLines (dir.File "out.txt"))

    [<Test>]
    member _.``stdinFile feeds the file to standard input`` () =
        use dir = new TempDir()
        File.WriteAllLines(dir.File "in.txt", [ "a"; "b" ])
        eq [ "> a"; "> b" ] (test [ "EchoStdin"; "> " ] |> Cmd.stdinFile (dir.File "in.txt") |> Cmd.lines |> Cmd.get)

    [<Test>]
    member _.``a missing stdin file throws before the command runs`` () =
        use dir = new TempDir()
        Assert.Throws<FileNotFoundException>(fun () ->
            test [ "FailTimes"; dir.File "count"; "0" ] |> Cmd.stdinFile (dir.File "missing.txt") |> Cmd.run) |> ignore
        Assert.False(File.Exists(dir.File "count"), "the command ran")

    [<Test>]
    member _.``sh strings redirect with < > and >>`` () =
        use dir = new TempDir()
        let input, output = dir.File "in file.txt", dir.File "out file.txt"
        File.WriteAllLines(input, [ "a"; "b" ])
        sh $"{bin} EchoStdin 1: < {input} | {bin} EchoStdin 2: > {output}" |> Cmd.run
        sh $"{bin} PrintArgs c >> {output}" |> Cmd.run
        eq [ "2:1:a"; "2:1:b"; "c" ] (readLines output)

    [<Test>]
    member _.``shellx shows redirects`` () =
        use dir = new TempDir()
        File.WriteAllLines(dir.File "in file", [ "a" ])
        let out, _ = captureConsole (fun () ->
            shellx {
                cd dir.Path
                sh $"{bin} EchoStdin < 'in file' > out"
            })
        Assert.Contains($"$ {bin} EchoStdin > out < \"in file\"", out)

    [<Test>]
    member _.``relative files in a shell resolve against its directory`` () =
        use dir = new TempDir()
        Directory.CreateDirectory(dir.File "sub") |> ignore
        File.WriteAllLines(dir.File "sub/in.txt", [ "x" ])
        shell {
            cd dir.Path
            pushd "sub"
            sh $"{bin} EchoStdin '> ' < in.txt > out.txt"
            popd
        }
        eq [ "> x" ] (readLines (dir.File "sub/out.txt"))

    // ---- allowFailure ----

    [<Test>]
    member _.``allowFailure accepts any exit code`` () =
        test [ "ExitWith"; "3" ] |> Cmd.allowFailure |> Cmd.run
        eq [ "x" ] (test [ "PrintArgs"; "x" ] |> Cmd.allowFailure |> Cmd.lines |> Cmd.get)
        Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "3" ] |> Cmd.run) |> ignore

    [<Test>]
    member _.``allowFailure covers commands piped into it`` () =
        test [ "ExitWith"; "7" ] |>| test [ "EchoStdin" ] |> Cmd.allowFailure |> Cmd.run
        // without it the upstream failure fails the pipe
        Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "7" ] |>| test [ "EchoStdin" ] |> Cmd.run) |> ignore

    [<Test>]
    member _.``allowFailure still throws on a timeout`` () =
        Assert.Throws<ProcExecException>(fun () ->
            cmd bin [ "SlowOutput" ] |> Cmd.timeout (TimeSpan.FromMilliseconds 300.) |> Cmd.allowFailure |> Cmd.run) |> ignore

    // ---- quiet ----

    [<Test>]
    member _.``quiet hides the output of a command that succeeds`` () =
        let out, err = captureConsole (fun () -> test [ "InterMixedOutAndError" ] |> Cmd.quiet |> Cmd.run)
        eq "" out
        eq "" err

    [<Test>]
    member _.``quiet prints the output of a command that fails, then throws`` () =
        use dir = new TempDir()
        let mutable thrown = false
        let out, err = captureConsole (fun () ->
            try test [ "FailTimes"; dir.File "count"; "1" ] |> Cmd.quiet |> Cmd.run
            with :? ProcExecException -> thrown <- true)
        Assert.True(thrown, "a quiet command that fails must still throw")
        Assert.Contains("attempt 1", out)
        Assert.Contains("failed 1", err)

    [<Test>]
    member _.``quiet covers the standard error of commands piped into it`` () =
        let _, err = captureConsole (fun () -> test [ "InterMixedOutAndError" ] |>| test [ "EchoStdin" ] |> Cmd.quiet |> Cmd.run)
        eq "" err

    [<Test>]
    member _.``a quiet capture still returns its lines`` () =
        eq [ "a" ] (test [ "PrintArgs"; "a" ] |> Cmd.quiet |> Cmd.lines |> Cmd.get)

    // ---- retry ----

    [<Test>]
    member _.``retry runs a failing command again until it succeeds`` () =
        use dir = new TempDir()
        test [ "FailTimes"; dir.File "count"; "2" ] |> Cmd.retry 2 |> Cmd.run
        eq "3" (File.ReadAllText(dir.File "count"))

    [<Test>]
    member _.``retry gives up after the last attempt`` () =
        use dir = new TempDir()
        Assert.Throws<ProcExecException>(fun () -> test [ "FailTimes"; dir.File "count"; "5" ] |> Cmd.retry 2 |> Cmd.quiet |> Cmd.run) |> ignore
        eq "3" (File.ReadAllText(dir.File "count"))

    [<Test>]
    member _.``retry works for captures and waits between attempts`` () =
        use dir = new TempDir()
        let sw = Diagnostics.Stopwatch.StartNew()
        let lines = test [ "FailTimes"; dir.File "count"; "1" ] |> Cmd.retry 1 |> Cmd.retryDelay (TimeSpan.FromMilliseconds 400.) |> Cmd.lines |> Cmd.get
        eq [ "attempt 2" ] lines
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds 400., "no delay between attempts")

    [<Test>]
    member _.``a retried command in a shell block runs again`` () =
        use dir = new TempDir()
        let count = dir.File "count"
        shell { sh $"{bin} FailTimes {count} 1" |> Cmd.retry 1 }
        eq "2" (File.ReadAllText count)

    [<Test>]
    member _.``retry can not be negative`` () =
        Assert.Throws<ArgumentException>(fun () -> test [] |> Cmd.retry -1 |> ignore) |> ignore

    // ---- exec { } keywords ----

    [<Test>]
    member _.``exec run supports files, quiet, retry and allow_failure`` () =
        use dir = new TempDir()
        File.WriteAllLines(dir.File "in.txt", [ "a" ])
        exec {
            binary bin
            arguments "EchoStdin" "> "
            stdin_file (dir.File "in.txt")
            to_file (dir.File "out.txt")
            run
        }
        exec { binary bin; arguments "PrintArgs" "b"; append_to_file (dir.File "out.txt"); run }
        eq [ "> a"; "b" ] (readLines (dir.File "out.txt"))
        exec { binary bin; arguments "FailTimes" (dir.File "count") "1"; retry 1; quiet; run }
        eq "2" (File.ReadAllText(dir.File "count"))
        exec { binary bin; arguments "ExitWith" "3"; allow_failure; run }
        exec { binary bin; arguments "EchoStdin" "> "; stdin [ "x" ]; to_file (dir.File "x.txt"); run }
        eq [ "> x" ] (readLines (dir.File "x.txt"))

    [<Test>]
    member _.``exec keywords other than run refuse what they can not do`` () =
        Assert.Throws<ArgumentException>(fun () -> exec { binary bin; arguments "PrintArgs" "a"; to_file "out.txt"; output } |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> exec { binary bin; arguments "ExitWith" "0"; retry 1; exit_code } |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> exec { binary bin; arguments "ExitWith" "0"; quiet; exit_code } |> ignore) |> ignore

    // ---- which and require ----

    [<Test>]
    member _.``which finds a program on the PATH`` () =
        Assert.Equal(Some bin, Cmd.which bin)
        Assert.Equal(None, Cmd.which "proc-fs-no-such-tool")
        Assert.True((Cmd.which (if OperatingSystem.IsWindows() then "cmd" else "sh")).IsSome)

    [<Test>]
    member _.``which in a shell uses the block's PATH`` () =
        use dir = new TempDir()
        let tool = makeTool dir "proc-fs-tool"
        let found, missing = shell {
            env "PATH" dir.Path
            let! found = which "proc-fs-tool"
            unsetEnv "PATH"
            let! missing = which "proc-fs-tool"
            return found, missing
        }
        eq (Some tool) found
        eq None missing

    [<Test>]
    member _.``which skips files that are not executable`` () =
        if not (OperatingSystem.IsWindows()) then
            use dir = new TempDir()
            File.WriteAllText(dir.File "proc-fs-plain", "")
            let found = shell {
                env "PATH" dir.Path
                return! which "proc-fs-plain"
            }
            eq None found

    [<Test>]
    member _.``require stops the block when a program is missing`` () =
        let ranAfter = ref false
        let ex = Assert.Throws<FileNotFoundException>(fun () ->
            shell {
                require bin
                require "proc-fs-no-such-tool"
                ranAfter.Value <- true
            })
        Assert.Contains("proc-fs-no-such-tool", ex.Message)
        Assert.False ranAfter.Value
