// For more information see https://aka.ms/fsharp-console-apps

open System

let args = Environment.GetCommandLineArgs()
printfn "%i" args.Length
for a in args do
    printfn "%s" a
