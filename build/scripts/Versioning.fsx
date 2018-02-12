#I @"../../packages/build/FAKE/tools"
#I @"../../packages/build/FSharp.Data/lib/net45"
#r @"FakeLib.dll"
#r @"FSharp.Data.dll"
#r @"System.Xml.Linq.dll"

#load @"Projects.fsx"
#load @"Paths.fsx"
#load @"Commandline.fsx"

open System
open System.Diagnostics
open System.IO
open System.Xml
open System.Text.RegularExpressions
open FSharp.Data

open Fake
open AssemblyInfoFile
open SemVerHelper
open Paths
open Projects
open SemVerHelper
open Commandline

module Versioning =
    type private GlobalJson = JsonProvider<"../../global.json">
    let globalJson = GlobalJson.Load("../../global.json");
    let private versionOf project =
        match project with
        | ObservableProcess -> globalJson.Versions.Observableprocess.Remove(0, 1)
        | ElasticsearchNode -> globalJson.Versions.Elasticsearchnode.Remove(0, 1)
        | ElasticsearchNodeRunner -> globalJson.Versions.ElasticsearchnodeRunner.Remove(0, 1)

    let private assemblyVersionOf v = sprintf "%i.0.0" v.Major |> parse

    let private assemblyFileVersionOf v = sprintf "%i.%i.%i.0" v.Major v.Minor v.Patch |> parse

    let writeVersionIntoGlobalJson project version =
        //write it with a leading v in the json, needed for the json type provider to keep things strings
        let pre (v: string) = match (v.StartsWith("v")) with | true -> v | _ -> sprintf "v%s" v
        let observableProcessVersion = match project with | ObservableProcess -> pre version | _ -> pre <| globalJson.Versions.Observableprocess
        let elasticsearchNodeVersion = match project with | ElasticsearchNode -> pre version | _ -> pre <| globalJson.Versions.Elasticsearchnode
        let elasticsearchNodeRunnerVersion = match project with | ElasticsearchNodeRunner -> pre version | _ -> pre <| globalJson.Versions.ElasticsearchnodeRunner
        let versionsNode = GlobalJson.Versions(observableProcessVersion, elasticsearchNodeVersion, elasticsearchNodeRunnerVersion)

        let newGlobalJson = GlobalJson.Root (GlobalJson.Sdk(globalJson.Sdk.Version), versionsNode)
        use tw = new StreamWriter("global.json")
        newGlobalJson.JsonValue.WriteTo(tw, JsonSaveOptions.None)
        traceImportant <| sprintf "Written (%O) to global.json as the current version will use this version from now on as current in the build" version

    type AssemblyVersionInfo = { Informational: SemVerInfo; Assembly: SemVerInfo; AssemblyFile: SemVerInfo; Project: ProjectInfo }
    let VersionInfo project =
        let currentVersion = versionOf project |> parse
        let bv = getBuildParam "version"
        let buildVersion = 
            match (Commandline.project) with 
            | p when p = project -> if (isNullOrEmpty bv) then None else Some(parse(bv))
            | _ -> Some(parse(versionOf project))
        let target =getBuildParam "target"
        let isCurrentProject = project = Commandline.project
        match (target, buildVersion, isCurrentProject) with
        | ("release", None, _) -> failwithf "can not run release because no explicit version number was passed on the command line"
        | ("release", Some v, true) ->
            if (currentVersion >= v) then failwithf "tried to create release %O for project %O but current version is already at %O" v project currentVersion
            { Informational= v; Assembly= assemblyVersionOf v; AssemblyFile = assemblyFileVersionOf v; Project = infoOf project }
        | _ ->
            tracefn "Not running 'release' target or %O is not the release current release target (%O) so using version in global.json (%O) as current" project (Commandline.project) currentVersion
            { Informational= currentVersion; Assembly= assemblyVersionOf currentVersion; AssemblyFile = assemblyFileVersionOf currentVersion; Project = infoOf project}

    let AllProjectVersions = Project.All |> Seq.map VersionInfo
    let ValidateArtifacts project =
        let pi = VersionInfo project
        let projectAssemblyFile = pi.AssemblyFile
        let projectAssembly = pi.Assembly
        traceFAKE "Assembly: %O AssemblyFile %O Informational: %O => project %s" 
            pi.Assembly pi.AssemblyFile pi.Informational (nameOf project) 

        let tmp = "build/output/_packages/tmp"
        !! "build/output/_packages/*.nupkg"
        |> Seq.iter(fun f ->
           Unzip tmp f
           !! (sprintf "%s/**/*.dll" tmp)
           |> Seq.iter(fun dll ->
                let fv = FileVersionInfo.GetVersionInfo(dll)
                let actualFileVersion = fv.FileVersion
                let actualProductVersion = fv.FileVersion
                let a = GetAssemblyVersion dll
                traceFAKE "Assembly: %A AssemblyFile: %s Informational: %s => %s" a fv.FileVersion fv.ProductVersion dll
                if (a.Minor > 0 || a.Revision > 0 || a.Build > 0) then failwith (sprintf "%s assembly version is not sticky to its major component" dll)
                if (parse (fv.ProductVersion) <> pi.Informational) then 
                    failwith <| sprintf "Expected product info %s to match new version %O " fv.ProductVersion pi.Informational

                let assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dll);
                if not <| assemblyName.FullName.Contains("PublicKeyToken=96c599bbe3e70f5d") then
                    failwith <| sprintf "%s should have PublicKeyToken=96c599bbe3e70f5d" assemblyName.FullName
           )
           DeleteDir tmp
        )
