module Proc.Fs.Tests.ExecTests

open System
open ProcNet
open Proc.Fs
open TUnit.Core

let private bin = TestBinary.path
let private wait = TimeSpan.FromSeconds 10.

let private lines (r: ProcessCaptureResult) = r.ConsoleOut |> Seq.map (fun l -> l.Line) |> List.ofSeq

type ExecTests() =

    [<Test>]
    member _.``exit_code returns a non-zero exit code instead of throwing`` () =
        let code = exec { exit_code_of bin "ExitWith" "3" }
        Assert.Equal(3, code)

    [<Test>]
    member _.``exit_code with arguments set separately returns a non-zero exit code`` () =
        let code = exec {
            binary bin
            arguments "ExitWith" "4"
            exit_code
        }
        Assert.Equal(4, code)

    [<Test>]
    member _.``exit_code still honours an explicit valid_exit_code`` () =
        Assert.Throws<ProcExecException>(fun () ->
            exec {
                binary bin
                arguments "ExitWith" "3"
                valid_exit_code (fun c -> c = 0)
                exit_code
            } |> ignore
        ) |> ignore

    [<Test>]
    member _.``run throws on a non-zero exit code`` () =
        let ex = Assert.Throws<ProcExecException>(fun () -> exec { run bin "ExitWith" "5" })
        Assert.Equal(Nullable 5, ex.ExitCode)

    [<Test>]
    member _.``valid_exit_code lets run accept a non-zero exit code`` () =
        exec {
            binary bin
            arguments "ExitWith" "5"
            valid_exit_code (fun c -> c = 5)
            run
        }

    [<Test>]
    member _.``obsolete validExitCode alias still works`` () =
        exec {
            binary bin
            arguments "ExitWith" "6"
            validExitCode (fun c -> c = 6)
            run
        }

    [<Test>]
    member _.``output captures console out and the exit code`` () =
        let result = exec {
            binary bin
            arguments "PrintArgs" "a" "with space"
            output
        }
        Assert.Equal(Nullable 0, result.ExitCode)
        Assert.Equal<string list>([ "a"; "with space" ], lines result)

    [<Test>]
    member _.``output_of accepts a list of arguments`` () =
        let result = exec { output_of bin [ "PrintArgs"; "x"; "y" ] }
        Assert.Equal<string list>([ "x"; "y" ], lines result)

    [<Test>]
    member _.``filter returns only the matching lines`` () =
        let matching = exec {
            binary bin
            arguments "PrintArgs" "keep-1" "drop" "keep-2"
            filter (fun l -> l.Line.StartsWith "keep")
        }
        Assert.Equal<string list>([ "keep-1"; "keep-2" ], matching |> List.map (fun l -> l.Line))

    [<Test>]
    member _.``captured processes that read stdin see end-of-input instead of hanging`` () =
        let result = exec {
            binary bin
            arguments "EchoStdin" "> "
            timeout wait
            output
        }
        Assert.True(result.Completed, "process waiting on stdin was killed by the timeout")
        Assert.Equal(Nullable 0, result.ExitCode)

    [<Test>]
    member _.``env and working_dir reach the child process`` () =
        let result = exec {
            binary bin
            arguments "PrintEnv" "PROC_FS_TEST"
            env (Map [ "PROC_FS_TEST", "hello" ])
            working_dir TestBinary.workingDir
            output
        }
        Assert.Equal<string list>([ "hello" ], lines result)

    [<Test>]
    member _.``timeout kills a run that takes too long`` () =
        let started = DateTime.UtcNow
        Assert.Throws<ProcExecException>(fun () ->
            exec {
                binary bin
                arguments "SlowOutput"
                timeout (TimeSpan.FromMilliseconds 500.)
                run
            }
        ) |> ignore
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds 4., "timeout was not applied")

    [<Test>]
    member _.``timeout marks captured output as not completed`` () =
        let result = exec {
            binary bin
            arguments "SlowOutput"
            timeout (TimeSpan.FromMilliseconds 500.)
            output
        }
        Assert.False(result.Completed)

    [<Test>]
    member _.``without a timeout the default waits for the process to finish`` () =
        Assert.Equal(None, ExecOptions.Empty.Timeout)
        let result = exec { output_of bin "SlowOutput" }
        Assert.True(result.Completed)
        Assert.Equal(Nullable 121, result.ExitCode)

    [<Test>]
    member _.``obsolete thread_wrap still compiles and has no effect`` () =
        exec {
            binary bin
            arguments "ExitWith" "0"
            thread_wrap false
            run
        }
