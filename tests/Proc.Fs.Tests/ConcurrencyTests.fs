module Proc.Fs.Tests.ConcurrencyTests

open System
open System.Diagnostics
open System.IO
open ProcNet
open Proc.Fs
open Proc.Fs.Tests.TestBinary
open TUnit.Core

let private bin = TestBinary.path
let private test args = cmd bin args |> Cmd.timeout (TimeSpan.FromSeconds 20.)

/// A background server: prints "ready" and its process id, then runs until stopped
let private server () = cmd bin [ "ReadyThenWait" ] |> Cmd.waitFor ((=) "ready")

let private isRunning (pid: int) =
    try not (Process.GetProcessById(pid).HasExited)
    with :? ArgumentException -> false

let private pidOf (background: Background) =
    match background.ProcessId with
    | Some pid -> pid
    | None -> failwith "the background process has no process id"

type BackgroundTests() =

    [<Test>]
    member _.``background waits for the ready line and use! stops the process at the end of the scope`` () =
        let pid = ref 0
        shell {
            do! steps {
                use! api = background (server ())
                pid.Value <- pidOf api
                Assert.True(isRunning pid.Value, "the background process is not running inside its scope")
                do! test [ "ExitWith"; "0" ]
            }
            Assert.False(isRunning pid.Value, "the background process still runs after its scope")
        }
        Assert.False(isRunning pid.Value)

    [<Test>]
    member _.``the block stops a background process bound with let!`` () =
        let pid = shell {
            let! api = background (server ())
            return pidOf api
        }
        Assert.False(isRunning pid, "the background process outlived the block")

    [<Test>]
    member _.``background uses the environment of the block`` () =
        let seen = shell {
            setEnv "PROC_FS_BACKGROUND" "from-the-shell"
            use! printer = background (cmd bin [ "PrintEnv"; "PROC_FS_BACKGROUND" ] |> Cmd.waitFor ((=) "from-the-shell"))
            return printer.Cmd.Environment |> Option.bind (Map.tryFind "PROC_FS_BACKGROUND")
        }
        Assert.Equal(Some "from-the-shell", seen)

    [<Test>]
    member _.``a background process that crashes fails the next command`` () =
        let ranAfter = ref false
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = background (cmd bin [ "ReadyThenExit"; "3"; "100" ] |> Cmd.waitFor ((=) "ready"))
                do! test [ "Sleep"; "1000" ]
                do! test [ "ExitWith"; "0" ]
                ranAfter.Value <- true
            })
        Assert.Equal(Nullable 3, ex.ExitCode)
        Assert.Contains("Background process exited with '3'", ex.Message)
        Assert.False(ranAfter.Value, "a command ran after the background process crashed")

    [<Test>]
    member _.``a background process that exits with 0 does not fail the block`` () =
        let ranAfter = ref false
        shell {
            use! _ = background (cmd bin [ "ReadyThenExit"; "0"; "100" ] |> Cmd.waitFor ((=) "ready"))
            do! test [ "Sleep"; "1000" ]
            do! test [ "ExitWith"; "0" ]
            ranAfter.Value <- true
        }
        Assert.True ranAfter.Value

    [<Test>]
    member _.``background throws when the process exits before it is ready`` () =
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = background (cmd bin [ "ExitWith"; "4" ] |> Cmd.waitFor ((=) "never printed"))
                return ()
            })
        Assert.Contains("before it was ready", ex.Message)
        Assert.Equal(Nullable 4, ex.ExitCode)

    [<Test>]
    member _.``background stops the process when it is not ready in time`` () =
        let pid = ref 0
        let notReady (line: string) =
            match Int32.TryParse line with
            | true, p -> pid.Value <- p
            | _ -> ()
            false
        let started = Stopwatch.StartNew()
        Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = background (cmd bin [ "ReadyThenWait" ] |> Cmd.waitFor notReady |> Cmd.timeout (TimeSpan.FromSeconds 1.))
                return ()
            }) |> ignore
        Assert.True(started.Elapsed < TimeSpan.FromSeconds 15., $"took {started.Elapsed}")
        Assert.NotEqual(0, pid.Value)
        Assert.False(isRunning pid.Value, "the process that was not ready in time still runs")

    [<Test>]
    member _.``wait throws when the background process fails`` () =
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                let! job = background (cmd bin [ "ReadyThenExit"; "5"; "50" ] |> Cmd.waitFor ((=) "ready"))
                do! wait job
            })
        Assert.Equal(Nullable 5, ex.ExitCode)

    [<Test>]
    member _.``wait returns once the background process exits with 0`` () =
        let exitCode = shell {
            let! job = background (cmd bin [ "ReadyThenExit"; "0"; "50" ] |> Cmd.waitFor ((=) "ready"))
            do! wait job
            do! test [ "ExitWith"; "0" ]
            return job.ExitCode
        }
        Assert.Equal(Some 0, exitCode)

type DeferTests() =

    [<Test>]
    member _.``defer runs at the end of the scope`` () =
        use dir = new TempDir()
        let marker = dir.File "deferred.txt"
        let existedInside = shell {
            use! _ = defer (test [ "PrintArgs"; "cleaned" ] |> Cmd.toFile marker)
            return File.Exists marker
        }
        Assert.False(existedInside, "the deferred command ran before the end of its scope")
        Assert.Equal("cleaned", (File.ReadAllText marker).Trim())

    [<Test>]
    member _.``defer runs when the scope fails`` () =
        use dir = new TempDir()
        let marker = dir.File "deferred.txt"
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = defer (test [ "PrintArgs"; "cleaned" ] |> Cmd.toFile marker)
                do! test [ "ExitWith"; "3" ]
            })
        Assert.Equal(Nullable 3, ex.ExitCode)
        Assert.True(File.Exists marker, "the deferred command did not run after the failure")

    [<Test>]
    member _.``the failure of the scope wins over a failing deferred command`` () =
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = defer (test [ "ExitWith"; "9" ])
                do! test [ "ExitWith"; "3" ]
            })
        Assert.Equal(Nullable 3, ex.ExitCode)

    [<Test>]
    member _.``a failing deferred command fails a scope that succeeded`` () =
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = defer (test [ "ExitWith"; "9" ])
                do! test [ "ExitWith"; "0" ]
            })
        Assert.Equal(Nullable 9, ex.ExitCode)

    [<Test>]
    member _.``defer keeps the directory of the moment it ran`` () =
        use dir = new TempDir()
        Directory.CreateDirectory(dir.File "sub") |> ignore
        shell {
            cd dir.Path
            use! _ = defer (test [ "PrintArgs"; "cleaned" ] |> Cmd.toFile "deferred.txt")
            cd "sub"
        }
        Assert.True(File.Exists(dir.File "deferred.txt"), "the deferred command did not run in the directory of defer")
        Assert.False(File.Exists(Path.Combine(dir.Path, "sub", "deferred.txt")))

type StepsTests() =

    [<Test>]
    member _.``steps does not run until it is used`` () =
        let ran = ref false
        let later = steps {
            do! test [ "ExitWith"; "0" ]
            ran.Value <- true
        }
        Assert.False(ran.Value, "steps ran when it was evaluated")
        shell { do! later }
        Assert.True ran.Value

    [<Test>]
    member _.``steps runs in the environment of the block that uses it`` () =
        let read = steps {
            let! value = test [ "PrintEnv"; "PROC_FS_STEPS" ] |> Cmd.text
            return value
        }
        let value = shell {
            setEnv "PROC_FS_STEPS" "inherited"
            let! value = read
            return value
        }
        Assert.Equal("inherited", value)

type ConcurrentlyTests() =

    [<Test>]
    member _.``concurrently runs the branches at the same time and keeps their order`` () =
        let started = Stopwatch.StartNew()
        let results = shell {
            let! results = concurrently [
                for name in [ "a"; "b"; "c" ] ->
                    steps {
                        do! test [ "Sleep"; "1500" ]
                        let! echoed = test [ "PrintArgs"; name ] |> Cmd.text
                        return echoed
                    }
            ]
            return results
        }
        Assert.Equal<string list>([ "a"; "b"; "c" ], results)
        // One after the other takes 4.5s or more
        Assert.True(started.Elapsed < TimeSpan.FromSeconds 4., $"took {started.Elapsed}")

    [<Test>]
    member _.``the first failing branch stops the others and its exception propagates`` () =
        let started = Stopwatch.StartNew()
        let ranAfter = ref false
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                let! _ = concurrently [
                    steps {
                        do! test [ "Sleep"; "30000" ]
                        ranAfter.Value <- true
                    }
                    steps { do! test [ "ExitWith"; "3" ] }
                ]
                return ()
            })
        Assert.Equal(Nullable 3, ex.ExitCode)
        Assert.False(ranAfter.Value, "the sibling of the failing branch went on")
        Assert.True(started.Elapsed < TimeSpan.FromSeconds 15., $"took {started.Elapsed}")

    [<Test>]
    member _.``a cd inside a branch does not reach the block`` () =
        use dir = new TempDir()
        let before, after = shell {
            let! before = pwd
            let! _ = concurrently [ steps { cd dir.Path } ]
            let! after = pwd
            return before, after
        }
        Assert.Equal(before, after)

    [<Test>]
    member _.``concurrentlyAtMost 1 runs one branch at a time`` () =
        let started = Stopwatch.StartNew()
        shell {
            let! _ = concurrentlyAtMost 1 [ steps { do! test [ "Sleep"; "800" ] }; steps { do! test [ "Sleep"; "800" ] } ]
            return ()
        }
        Assert.True(started.Elapsed >= TimeSpan.FromSeconds 1.6, $"took {started.Elapsed}")

    [<Test>]
    member _.``a background process started in a branch stops when the branch ends`` () =
        let pids = shell {
            let! pids = concurrently [
                steps {
                    let! api = background (server ())
                    return pidOf api
                }
            ]
            return pids
        }
        Assert.All(pids, fun pid -> Assert.False(isRunning pid, "the background process outlived its branch"))

    [<Test>]
    member _.``a background process of the block that crashes fails a branch`` () =
        let ex = Assert.Throws<ProcExecException>(fun () ->
            shell {
                use! _ = background (cmd bin [ "ReadyThenExit"; "6"; "100" ] |> Cmd.waitFor ((=) "ready"))
                let! _ = concurrently [
                    steps {
                        do! test [ "Sleep"; "1000" ]
                        do! test [ "ExitWith"; "0" ]
                    }
                ]
                return ()
            })
        Assert.Equal(Nullable 6, ex.ExitCode)
