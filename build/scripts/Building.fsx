#I @"../../packages/build/FAKE/tools"
#I @"../../packages/build/FSharp.Data/lib/net45"
#r @"FakeLib.dll"
#r @"FSharp.Data.dll"

#load @"Paths.fsx"
#load @"Tooling.fsx"
#load @"Versioning.fsx"

open System 
open System.IO
open Fake 
open FSharp.Data 

open Paths
open Projects
open Tooling
open Versioning

module Build =

    let private runningRelease = hasBuildParam "version" || hasBuildParam "apikey" || getBuildParam "target" = "release"

    type private GlobalJson = JsonProvider<"../../global.json">
    let private pinnedSdkVersion = GlobalJson.GetSample().Sdk.Version

    let private compileCore incremental =
        if not (DotNetCli.isInstalled()) then failwith  "You need to install the dotnet command line SDK to build for .NET Core"
        let runningSdkVersion = DotNetCli.getVersion()
        if (runningSdkVersion <> pinnedSdkVersion) then failwithf "Attempting to run with dotnet.exe with %s but global.json mandates %s" runningSdkVersion pinnedSdkVersion

        let props =
            Projects.Project.All
            |> Seq.collect (fun p -> 
                let name = (nameOf p).Replace(".", "")
                let version = Versioning.VersionInfo p
                [ 
                    sprintf "%sCurrentVersion" name , (version.Informational.ToString());
                    sprintf "%sCurrentAssemblyVersion" name, (version.Assembly.ToString());
                    sprintf "%sCurrentAssemblyFileVersion" name, (version.AssemblyFile.ToString());
                ] 
            )
            |> Seq.map (fun (p,v) -> sprintf "%s=%s" p v)
            |> String.concat ";"
            |> sprintf "/property:%s"
        
        DotNetCli.Build
            (fun p -> 
                { p with 
                    Configuration = "Release" 
                    Project = Paths.SolutionFile
                    TimeOut = TimeSpan.FromMinutes(3.)
                    AdditionalArgs = [props]
                }
            ) |> ignore

    let Restore() =
        DotNetCli.Restore
            (fun p -> 
                { p with 
                    Project = Paths.SolutionFile
                    TimeOut = TimeSpan.FromMinutes(3.)
                }
            ) |> ignore
        
    let Compile incremental = 
        compileCore incremental

    let Clean() =
        CleanDir Paths.BuildOutput
        let cleanCommand = sprintf "clean %s -c Release" Paths.SolutionFile
        DotNetCli.RunCommand (fun p -> { p with TimeOut = TimeSpan.FromMinutes(3.) }) cleanCommand |> ignore
        //DotNetProject.All |> Seq.iter(fun p -> CleanDir(Paths.BinFolder p.Name))
