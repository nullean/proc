open Proc.Fs

let _ = shell {
    exec "dotnet" "--version"
    exec "uname"
}

let dotnetVersion = exec {
    binary "dotnet"
    args "--help"
    filter (fun l -> l.Line.Contains "clean")
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
