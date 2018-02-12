#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"

open System
open Fake

[<AutoOpen>]
module Projects = 

    type Project =
        | ObservableProcess
        | ElasticsearchNode
        | ElasticsearchNodeRunner

        static member All = [ObservableProcess; ElasticsearchNode; ElasticsearchNodeRunner]

    type ProjectInfo = { name: string; project: Project}

    let nameOf project = 
        match project with
        | ObservableProcess -> "ObservableProcess"
        | ElasticsearchNode -> "ElasticsearchNode"
        | ElasticsearchNodeRunner -> "ElasticsearchNode.Runner"

    let infoOf project = { name = project |> nameOf; project = project }
    let projectsStartingWith partial =
        Project.All 
        |> Seq.map nameOf 
        |> Seq.filter (fun s -> s |> toLower |> startsWith (partial |> toLower) && partial |> isNotNullOrEmpty) 
        |> Seq.toList

    let tryFind partial =
        let projectsStartingWith = 
            Project.All 
            |> Seq.map infoOf
            |> Seq.filter (fun s -> partial |> isNotNullOrEmpty && s.name |> toLower |> startsWith (partial |> toLower)) 
            |> Seq.toList

        match projectsStartingWith with 
        | [i] -> Some i.project
        | _ -> None


