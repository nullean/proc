module Targets

open Argu
open System
open System.IO
open Bullseye
open CommandLine
open Fake.Tools.Git
open ProcNet

    
let exec binary args =
    Proc.Exec (binary, args |> List.toArray)
    
let private restoreTools = lazy(exec "dotnet" ["tool"; "restore"])
let private currentVersion =
    lazy(
        restoreTools.Value |> ignore
        let r = Proc.Start("dotnet", "minver", "-p", "canary.0", "-m", "0.1")
        let o = r.ConsoleOut |> Seq.find (fun l -> not(l.Line.StartsWith("MinVer:")))
        o.Line
    )
let private currentVersionInformational =
    lazy(
        match Paths.IncludeGitHashInInformational with
        | false -> currentVersion.Value
        | true -> sprintf "%s+%s" currentVersion.Value (Information.getCurrentSHA1( "."))
    )

let private clean (arguments:ParseResults<Arguments>) =
    if (Paths.Output.Exists) then Paths.Output.Delete (true)
    // clean every project but this build itself: `dotnet clean` on the solution would delete the assemblies the
    // running build still loads lazily (Fake, Proc) and fail the next target
    ["src"; "tests"; "examples"]
    |> Seq.collect (fun d -> Directory.EnumerateFiles(d, "*.?sproj", SearchOption.AllDirectories))
    |> Seq.iter (fun p -> exec "dotnet" ["clean"; p; "-c"; "Release"; "-v"; "q"] |> ignore)
    
let private build (arguments:ParseResults<Arguments>) = exec "dotnet" ["build"; "-c"; "Release"] |> ignore

let private test (arguments:ParseResults<Arguments>) =
    exec "dotnet" ["test"; "-c"; "RELEASE"; "--logger:GithubActions"; "--logger:pretty"] |> ignore

let private generatePackages (arguments:ParseResults<Arguments>) =
    let output = Paths.RootRelative Paths.Output.FullName
    exec "dotnet" ["pack"; "-c"; "Release"; "-o"; output] |> ignore
    
let private validatePackages (arguments:ParseResults<Arguments>) =
    let output = Paths.RootRelative <| Paths.Output.FullName
    let nugetPackages =
        Paths.Output.GetFiles("*.nupkg") |> Seq.sortByDescending(fun f -> f.CreationTimeUtc)
        |> Seq.map (fun p -> Paths.RootRelative p.FullName)
        
    let jenkinsOnWindowsArgs =
        if Fake.Core.Environment.hasEnvironVar "JENKINS_URL" && Fake.Core.Environment.isWindows then ["-r"] else []
    
    let args = ["-v"; currentVersionInformational.Value; "-k"; Paths.SignKey; "-t"; output] @ jenkinsOnWindowsArgs
    // nupkg-validator 0.11+ ships per-RID NativeAOT packages, so it validates net11.0 dlls without needing a matching
    // runtime. Its RID packages (nupkg-validator.linux-x64, ...) only live on nuget.org, not on the GitHub Packages feed CI
    // configures, so it runs through `dotnet tool exec` pinned to nuget.org rather than through the tool manifest.
    let validator = ["tool"; "exec"; "nupkg-validator@0.11.4"; "--source"; "https://api.nuget.org/v3/index.json"; "--"; "validate"]
    nugetPackages |> Seq.iter (fun p -> exec "dotnet" (validator @ [p] @ args) |> ignore)
    

let private generateApiChanges (arguments:ParseResults<Arguments>) =
    let output = Paths.RootRelative <| Paths.Output.FullName
    let currentVersion = currentVersion.Value
    let nugetPackages =
        Paths.Output.GetFiles("*.nupkg") |> Seq.sortByDescending(fun f -> f.CreationTimeUtc)
        |> Seq.map (fun p -> Path.GetFileNameWithoutExtension(Paths.RootRelative p.FullName).Replace("." + currentVersion, ""))
    nugetPackages
    |> Seq.iter(fun p ->
        let outputFile =
            let f = sprintf "breaking-changes-%s.md" p
            Path.Combine(output, f)
        // Proc.Fs only targets modern .NET: compare its net10.0 build against the previous netstandard2.0 package
        let tfm =
            if Directory.Exists(sprintf "src/%s/bin/Release/%s" p Paths.MainTFM) then Paths.MainTFM else "net10.0"
        let args =
            [
                "assembly-differ"
                (sprintf "previous-nuget|%s|%s|%s" p currentVersion Paths.MainTFM);
                (sprintf "directory|src/%s/bin/Release/%s" p tfm);
                "-a"; "true"; "--target"; p; "-f"; "github-comment"; "--output"; outputFile
            ]
        
        exec "dotnet" args |> ignore
    )
    
let Setup (parsed:ParseResults<Arguments>) (subCommand:Arguments) =
    let step (name:string) action = Targets.Target(name, new Action(fun _ -> action(parsed)))
    
    let cmd (name:string) commandsBefore steps action =
        let singleTarget = (parsed.TryGetResult SingleTarget |> Option.defaultValue false)
        let deps =
            match (singleTarget, commandsBefore) with
            | (true, _) -> [] 
            | (_, Some d) -> d
            | _ -> []
        let steps = steps |> Option.defaultValue []
        Targets.Target(name, deps @ steps, Action(action))
        
    step Clean.Name clean
    cmd Build.Name None (Some [Clean.Name]) <| fun _ -> build parsed
    
    cmd Test.Name (Some [Build.Name;]) None <| fun _ -> test parsed
    
    cmd Pack.Name
        (Some [Build.Name;])
        (Some [GeneratePackages.Name; ValidatePackages.Name; GenerateApiChanges.Name])
        ignore
    cmd Ci.Name
        (Some [Test.Name;])
        (Some [GeneratePackages.Name; ValidatePackages.Name; GenerateApiChanges.Name])
        ignore

    step GeneratePackages.Name generatePackages 
    step ValidatePackages.Name validatePackages 
    step GenerateApiChanges.Name generateApiChanges
