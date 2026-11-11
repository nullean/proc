module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Test
    | [<CliPrefix(CliPrefix.None);SubCommand>] Pack
    | [<CliPrefix(CliPrefix.None);SubCommand>] Ci
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages 
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges 
    
    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean -> "clean known output locations"
            | Build -> "Run build"
            | Test -> "Runs build then tests"
            | Pack -> "Runs build, then creates and validates the packages and reports public API changes"
            | Ci -> "Runs tests, then creates and validates the packages: exactly what CI runs"
            
            | SingleTarget _ -> "Runs the provided sub command without running their dependencies"
            
            | GeneratePackages
            | ValidatePackages
            | GenerateApiChanges
                -> "Undocumented, dependent target"
    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
