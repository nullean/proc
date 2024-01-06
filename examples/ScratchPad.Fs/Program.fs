﻿open System
open Proc.Fs

let _ = shell {
    exec "dotnet" "--version"
    exec "uname"
}

let dotnetVersion = exec {
    binary "dotnet"
    args "--help"
    filter_output (fun l -> l.Line.Contains "clean")
    filter (fun l -> l.Line.Contains "clean")
}

exec {
    binary "dotnet"
    args "--help"
    env Map[("key", "value")]
    workingDirectory "."
    send_control_c false
    timeout (TimeSpan.FromSeconds(10))
    thread_wrap false
    validExitCode (fun i -> i <> 0)
    run
}

let helpStatus = exec {
    binary "dotnet"
    args "--help"
    exit_code
}

let helpOutput = exec {
    binary "dotnet"
    args "--help"
    output
}

printfn "Found lines %i" dotnetVersion.Length

exec {
    binary "dotnet"
    run_args ["restore"; "--help"]
}

exec { run "dotnet" " "}
let statusCode = exec { exit_code_of "dotnet" " "}
