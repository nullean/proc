module Paths

open System
open System.IO

let ToolName = "proc"
let Repository = sprintf "nullean/%s" ToolName
let MainTFM = "netstandard2.0"
let SignKey = "96c599bbe3e70f5d"

let ValidateAssemblyName = true
let IncludeGitHashInInformational = true
let GenerateApiChanges = true

let Root =
    let mutable dir = DirectoryInfo(".")
    while dir.GetFiles("*.sln").Length = 0 do dir <- dir.Parent
    Environment.CurrentDirectory <- dir.FullName
    dir
    
let RootRelative path = Path.GetRelativePath(Root.FullName, path) 
    
let Output = DirectoryInfo(Path.Combine(Root.FullName, "build", "output"))

let ToolProject = DirectoryInfo(Path.Combine(Root.FullName, "src", ToolName))
