module Proc.Fs.Tests.CmdTests

open System
open System.Diagnostics
open ProcNet
open Proc.Fs
open Xunit

let private bin = TestBinary.path
let private test args = cmd bin args |> Cmd.timeout (TimeSpan.FromSeconds 20.)

[<Fact>]
let ``lines captures standard out only`` () =
    // FailTimes prints "attempt 1" to standard out and "failed 1" to standard error
    use dir = new TestBinary.TempDir()
    let lines = test [ "FailTimes"; dir.File "count"; "1" ] |> Cmd.allowFailure |> Cmd.lines |> Cmd.get
    Assert.Equal<string list>([ "attempt 1" ], lines)

[<Fact>]
let ``text joins the lines and trims whitespace`` () =
    let text = test [ "PrintArgs"; "  first"; "second  " ] |> Cmd.text |> Cmd.get
    Assert.Equal("first\nsecond", text)

[<Fact>]
let ``a capture throws when the command fails`` () =
    let ex = Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "3" ] |> Cmd.lines |> Cmd.get |> ignore)
    Assert.Equal(Nullable 3, ex.ExitCode)

[<Fact>]
let ``exitCode returns a non-zero exit code`` () =
    Assert.Equal(3, test [ "ExitWith"; "3" ] |> Cmd.exitCode |> Cmd.get)

[<Fact>]
let ``result returns output and exit code without throwing`` () =
    let r = test [ "ExitWith"; "4" ] |> Cmd.result |> Cmd.get
    Assert.Equal(Nullable 4, r.ExitCode)
    Assert.True r.Completed

[<Fact>]
let ``where, map and choose transform the captured lines`` () =
    let args = test [ "PrintArgs"; "a1"; "b2"; "a3" ]
    Assert.Equal<string list>([ "a1"; "a3" ], args |> Cmd.lines |> Cmd.where (fun l -> l.StartsWith "a") |> Cmd.get)
    Assert.Equal<string list>([ "A1"; "B2"; "A3" ], args |> Cmd.lines |> Cmd.map (fun l -> l.ToUpperInvariant()) |> Cmd.get)
    Assert.Equal<int list>([ 1; 3 ], args |> Cmd.lines |> Cmd.choose (fun l -> if l.StartsWith "a" then Some (int l[1..]) else None) |> Cmd.get)

[<Fact>]
let ``run throws when the command fails`` () =
    let ex = Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "5" ] |> Cmd.run)
    Assert.Equal(Nullable 5, ex.ExitCode)

[<Fact>]
let ``a timeout makes a capture throw`` () =
    let sw = Stopwatch.StartNew()
    Assert.Throws<ProcExecException>(fun () ->
        cmd bin [ "SlowOutput" ] |> Cmd.timeout (TimeSpan.FromMilliseconds 500.) |> Cmd.lines |> Cmd.get |> ignore
    ) |> ignore
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds 4., "timeout was not applied")

[<Fact>]
let ``iter receives lines while the process is still running`` () =
    // SlowOutput prints a line every 500ms for 5 seconds, then exits with 121
    let sw = Stopwatch.StartNew()
    let seen = ResizeArray<string * TimeSpan>()
    test [ "SlowOutput" ] |> Cmd.validExitCode ((=) 121) |> Cmd.iter (fun l -> seen.Add((l, sw.Elapsed)))
    Assert.Equal(10, seen.Count)
    let _, firstSeenAt = seen[0]
    Assert.True(firstSeenAt < TimeSpan.FromSeconds 3., $"first line only arrived after {firstSeenAt}")
    Assert.True(sw.Elapsed > TimeSpan.FromSeconds 4.)

[<Fact>]
let ``iter rethrows an exception from the callback and stops the process`` () =
    let sw = Stopwatch.StartNew()
    let ex = Assert.Throws<InvalidOperationException>(fun () ->
        test [ "SlowOutput" ] |> Cmd.iter (fun _ -> invalidOp "stop"))
    Assert.Equal("stop", ex.Message)
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds 4., "process kept running after the callback threw")

[<Fact>]
let ``iter throws when the command fails`` () =
    Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "2" ] |> Cmd.iter ignore) |> ignore

[<Fact>]
let ``stdin writes lines to the process`` () =
    let lines = test [ "EchoStdin"; "> " ] |> Cmd.stdin [ "a"; "b" ] |> Cmd.lines |> Cmd.get
    Assert.Equal<string list>([ "> a"; "> b" ], lines)

[<Fact>]
let ``a pipe streams standard out into the next command`` () =
    let lines = test [ "PrintArgs"; "a"; "with space" ] |>| test [ "EchoStdin"; "> " ] |> Cmd.lines |> Cmd.get
    Assert.Equal<string list>([ "> a"; "> with space" ], lines)

[<Fact>]
let ``pipes chain across three commands`` () =
    let lines =
        test [ "PrintArgs"; "x"; "y" ]
        |>| test [ "EchoStdin"; "1:" ]
        |>| test [ "EchoStdin"; "2:" ]
        |> Cmd.lines
        |> Cmd.get
    Assert.Equal<string list>([ "2:1:x"; "2:1:y" ], lines)

[<Fact>]
let ``a failing upstream command fails the pipe`` () =
    let ex = Assert.Throws<ProcExecException>(fun () ->
        test [ "ExitWith"; "7" ] |>| test [ "EchoStdin"; "> " ] |> Cmd.lines |> Cmd.get |> ignore)
    Assert.Equal(Nullable 7, ex.ExitCode)
    // The message names the stage that failed, not the whole pipe
    Assert.Contains("ExitWith 7\"", ex.Message)

[<Fact>]
let ``a failing upstream command fails a piped run`` () =
    let ex = Assert.Throws<ProcExecException>(fun () -> test [ "ExitWith"; "7" ] |>| test [ "EchoStdin"; "> " ] |> Cmd.run)
    Assert.Equal(Nullable 7, ex.ExitCode)

[<Fact>]
let ``a failing downstream command fails the pipe`` () =
    let ex = Assert.Throws<ProcExecException>(fun () -> test [ "PrintArgs"; "a" ] |>| test [ "ExitWith"; "9" ] |> Cmd.run)
    Assert.Equal(Nullable 9, ex.ExitCode)

[<Fact>]
let ``a downstream command that exits early stops the upstream command without failing`` () =
    let sw = Stopwatch.StartNew()
    test [ "SlowOutput" ] |>| test [ "ExitWith"; "0" ] |> Cmd.run
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds 4., "upstream kept running after downstream exited")

[<Fact>]
let ``a downstream command that exits early stops a silent upstream command too`` () =
    // DelayedWriter prints nothing for 3 seconds, so no failed write to the closed pipe can stop it
    let sw = Stopwatch.StartNew()
    test [ "DelayedWriter" ] |>| test [ "ExitWith"; "0" ] |> Cmd.run
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds 2., $"upstream kept running for {sw.Elapsed} after downstream exited")

[<Fact>]
let ``exec {} runs a piped command, and keywords that can not feed standard input refuse it`` () =
    let piped = test [ "PrintArgs"; "a" ] |>| test [ "ExitWith"; "4" ]
    let ex = Assert.Throws<ProcExecException>(fun () -> exec { options piped; run })
    Assert.Equal(Nullable 4, ex.ExitCode)
    Assert.Throws<ArgumentException>(fun () -> exec { options piped; output } |> ignore) |> ignore
