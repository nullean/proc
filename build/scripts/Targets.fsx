#load @"Projects.fsx"
#load @"Commandline.fsx"
#load @"Paths.fsx"
#load @"Tooling.fsx"
#load @"Versioning.fsx"
#load @"Testing.fsx"
#load @"Signing.fsx"
#load @"Building.fsx"
#load @"Releasing.fsx"

open System
open Fake

open Projects
open Paths
open Building
open Testing
open Versioning
open Releasing
open Signing
open Commandline

Commandline.parse()

Target "Build" <| fun _ -> traceHeader "STARTING BUILD"

Target "Clean" Build.Clean

Target "Restore" Build.Restore

Target "FullBuild" <| fun _ -> Build.Compile false
    
Target "Test" Tests.RunUnitTests

Target "ChangeVersion" <| fun _ -> 
    let newVersion = getBuildParam "version"
    Versioning.writeVersionIntoGlobalJson Commandline.project newVersion

Target "Version" <| fun _ -> 
    for v in Versioning.AllProjectVersions do
        traceImportant (sprintf "project %s has version %s from here on out" (v.Project.name) (v.Informational.ToString()))

Target "Release" <| fun _ -> 
    Release.CreateNugetPackage Commandline.project
    Versioning.ValidateArtifacts Commandline.project

// Dependencies
"Clean"
    =?> ("ChangeVersion", hasBuildParam "version")
    ==> "Version"
    ==> "Restore"
    ==> "FullBuild"
    =?> ("Test", (not Commandline.skipTests))
    ==> "Build"

"Build"
  ==> "Release"

RunTargetOrListTargets()

