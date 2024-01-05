open Proc.Fs

let uname = shell {
    exec "dotnet" "--version"
    exec "uname" 
}

let dotnetVersion = exec {
    binary "dotnet"
    args "--help"
    filter (fun l -> l.Line.Contains "clean")
}

printfn "Found lines %i" dotnetVersion.Length

let dotnetRestoreHelp = exec {
    binary "dotnet"
    invoke_args ["restore"; "--help"]
}
