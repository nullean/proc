open System
open Proc.Fs

let _ = shell {
    exec "dotnet" "--version"
    exec "uname"
}


exec { run "dotnet" "--help"}
exec {
    binary "dotnet"
    arguments "--help"
    env Map[("key", "value")]
    working_dir "."
    send_control_c false
    timeout (TimeSpan.FromSeconds(10))
    thread_wrap false
    validExitCode (fun i -> i = 0)
    run
}

let helpStatus = exec {
    binary "dotnet"
    arguments "--help"
    exit_code
}

let helpOutput = exec {
    binary "dotnet"
    arguments "--help"
    output
}
let dotnetVersion = exec {
    binary "dotnet"
    arguments "--help"
    filter_output (fun l -> l.Line.Contains "clean")
    filter (fun l -> l.Line.Contains "clean")
}

printfn "Found lines %i" dotnetVersion.Length


let dotnetOptions = exec { binary "dotnet" }
exec {
    options dotnetOptions
    run_args ["restore"; "--help"]
}

let args: string list = ["--help"]
exec { run "dotnet" "--help"}
exec { run "dotnet" args }

let _ = shell { exec "dotnet" args }
let statusCode = exec { exit_code_of "dotnet" "--help"}


printfn "That's all folks!"
