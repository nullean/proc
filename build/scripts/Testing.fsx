#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"

#load @"Projects.fsx"
#load @"Commandline.fsx"
#load @"Paths.fsx"
#load @"Tooling.fsx"

open System.IO
open Fake 
open Paths
open Projects
open Tooling
open Commandline

module Tests =
    open System

    let private dotnetTest() =
        CreateDir Paths.BuildOutput
        let command = ["xunit"; "-parallel"; "none"; "-xml"; "../.." @@ Paths.Output("ObservableProcess.Tests.xml")] 

        let dotnet = Tooling.BuildTooling("dotnet")
        dotnet.ExecIn "src/ObservableProcess.Tests" command |> ignore

    let RunUnitTests() = dotnetTest()

