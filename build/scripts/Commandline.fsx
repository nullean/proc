#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"
#load @"Projects.fsx"

open System
open Fake
open Projects

let private usage = """
USAGE:

build [project] <target> [params] [skiptests]

Targets:

* build
  - default target if non provided. 
* release <version>
  - 0 create a release worthy nuget packages for [version] under build\output
"""

module Commandline =

    let private args = getBuildParamOrDefault "cmdline" "" |> split ' '
    let skipTests = args |> List.exists (fun x -> x = "skiptests")
    let private arguments = args |> List.filter (fun x -> x <> "skiptests")

    let private (|IsAProject|_|) candidate =
        let names = projectsStartingWith candidate 
        match names with 
        | [name] -> Some name
        | [] ->
            traceError (sprintf "'%s' did not match any of our known projects '%A'" candidate (Project.All |> Seq.map nameOf))
            exit 2
            None
        | _ ->
            traceError (sprintf "'%s' yield more then one project '%A'" candidate names)
            exit 2
            None
        
    let private (|IsATarget|_|) (candidate: string) =
        let isTarget = Fake.TargetHelper.getAllTargetsNames() |> List.exists((=)candidate) 
        match isTarget with 
        | true -> Some candidate
        | _ ->
            None
    let project = 
        let p = 
            match arguments with
            | [IsAProject project] -> project |> tryFind
            | IsAProject project::tail -> project |> tryFind
            | _ ->
                traceError usage
                exit 2

        //we'll already have printed an error message and exited before `None` here triggers
        match p with | Some p -> p | _ -> raise <| ArgumentNullException();

    let target = 
        match arguments with
        | [IsAProject project] -> "build"
        | [IsAProject project; t] -> t
        | IsAProject project::t::tail -> t
        | _ ->
            traceError usage
            exit 2

    let parse () =
        printfn "%A" arguments
        match arguments with
        | [IsAProject project] -> ignore()
        | [IsAProject project; "release"; version] -> 
            setBuildParam "version" version
        | [IsAProject project; IsATarget t] when target |> isNotNullOrEmpty -> ignore()
        | _ ->
            traceError usage
            exit 2

        setBuildParam "target" target
        traceHeader (sprintf "%s - %s" (project |> nameOf) target)
