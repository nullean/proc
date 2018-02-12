#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"

#load @"Projects.fsx"
#load @"Paths.fsx"
#load @"Tooling.fsx"
#load @"Versioning.fsx"

open System
open System.Text
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Fake
open Fake.Paket

open Paths
open Projects
open Tooling
open Versioning

module Release = 
    let t = 1
    let CreateNugetPackage project = 

        let version = Versioning.VersionInfo project
        Pack (fun p -> 
        { 
            p with 
                Version = version.Informational.ToString()
                TemplateFile = Paths.Source <| nameOf project @@ "paket.template"
                OutputPath = Paths.PackageOutFolder
        })