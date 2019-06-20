#I @"../../packages/build/FAKE/tools"
#I @"../../packages/build/FSharp.Data/lib/net40"
#I @"../../packages/build/Mono.Cecil/lib/net40"
#r @"FakeLib.dll"
#r @"Mono.Cecil.dll"
#r @"FSharp.Data.dll"
#nowarn "0044" //TODO sort out FAKE 5

#load @"Paths.fsx"
#load @"Tooling.fsx"
#load @"Versioning.fsx"

open System 
open System.IO
open System.Reflection
open Fake 
open FSharp.Data 
open Mono.Cecil

open Paths
open Projects
open Tooling
open Versioning

module Build =

    type private GlobalJson = JsonProvider<"../../global.json", InferTypesFromValues = false>
    let private pinnedSdkVersion = GlobalJson.GetSample().Sdk.Version

    let private compileCore () =
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
            |> Seq.append [
                "FakeBuild", "1";
                "OutputPathBaseDir", Path.GetFullPath Paths.BuildOutput;
            ]
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
    let private buildEmbeddable () = 
        DotNetCli.Publish
            (fun p -> 
                { p with
                    Configuration = "Release"
                    Project = Paths.Source("Proc.ControlC") 
                }
            )
        File.Copy (@"src\Proc.ControlC\bin\release\net40\publish\Proc.ControlC.exe", @"src\Proc\Embedded\Proc.ControlC.exe", true)

    let Restore() =
        DotNetCli.Restore
            (fun p -> 
                { p with 
                    Project = Paths.SolutionFile
                    TimeOut = TimeSpan.FromMinutes(3.)
                }
            ) |> ignore
        
    let Compile () = 
        buildEmbeddable()
        compileCore()

    let Clean() =
        CleanDir Paths.BuildOutput
        let cleanCommand = sprintf "clean %s -c Release" Paths.SolutionFile
        DotNetCli.RunCommand (fun p -> { p with TimeOut = TimeSpan.FromMinutes(3.) }) cleanCommand |> ignore
        //DotNetProject.All |> Seq.iter(fun p -> CleanDir(Paths.BinFolder p.Name))
        
    type CustomResolver(folder) = 
        inherit DefaultAssemblyResolver()
        member this.Folder = folder;
        override this.Resolve name = 
            try
                base.Resolve name
            with
            | ex -> 
                AssemblyDefinition.ReadAssembly(Path.Combine(folder, "Elasticsearch.Net.dll"));

    let private rewriteNamespace tfm = 
        trace "Rewriting namespaces"
        let proc = nameOf Project.Proc
        let folder = sprintf "%s/%s" (Paths.Output proc) tfm
        let dll = sprintf "%s/%s.dll" folder proc

        use resolver = new CustomResolver(folder)
        let readerParams = new ReaderParameters( AssemblyResolver = resolver, ReadWrite = true );
        use assembly = AssemblyDefinition.ReadAssembly(dll, readerParams);
                
        for item in assembly.MainModule.Types do
            if item.Namespace.StartsWith("System.Reactive") then
                item.Namespace <- item.Namespace.Replace("System.Reactive", "ProcNet.Reactive")
                        
                // Touch custom attribute arguments 
                // Cecil does not update the types referenced within these attributes automatically,
                // so enumerate them to ensure namespace renaming is reflected in these references.
        let touchAttributes (attributes:Mono.Collections.Generic.Collection<CustomAttribute>) = 
            for attr in attributes do
                if attr.HasConstructorArguments then
                    for constArg in attr.ConstructorArguments do
                        if constArg.Type.Name = "Type" then ignore()    
        
                // rewrite explicitly implemented interface definitions defined
                // in Newtonsoft.Json
        let rewriteName (method:IMemberDefinition) =
            if method.Name.Contains("System.Reactive") then
                method.Name <- method.Name.Replace("System.Reactive", "ProcNet.Reactive")
                     
                // recurse through all types and nested types   
        let rec rewriteTypes (types:Mono.Collections.Generic.Collection<TypeDefinition>) =
            for t in types do
                touchAttributes t.CustomAttributes
                for prop in t.Properties do 
                    touchAttributes prop.CustomAttributes
                    rewriteName prop
                    if prop.GetMethod <> null then rewriteName prop.GetMethod
                    if prop.SetMethod <> null then rewriteName prop.SetMethod
                for method in t.Methods do 
                    touchAttributes method.CustomAttributes
                    rewriteName method
                    for over in method.Overrides do rewriteName method
                for field in t.Fields do touchAttributes field.CustomAttributes
                for interf in t.Interfaces do touchAttributes interf.CustomAttributes
                for event in t.Events do touchAttributes event.CustomAttributes
                if t.HasNestedTypes then rewriteTypes t.NestedTypes
                        
        assembly.MainModule.Types |> rewriteTypes
                
        let resources = assembly.MainModule.Resources
        for i = resources.Count-1 downto 0 do
            let resource = resources.[i]
            // remove the Newtonsoft signing key
            if resource.Name = "Newtonsoft.Json.Dynamic.snk" then resources.Remove(resource) |> ignore
            printfn "%s" resource.Name

        
        let key = File.ReadAllBytes(Paths.Keys("keypair.snk"))
        let kp = StrongNameKeyPair(key)
        let wp = WriterParameters ( StrongNameKeyPair = kp);
        assembly.Write(wp) |> ignore;
        tracefn "Finished rewriting namespaces for %s" tfm
        
    let private ilRepackInternal tfm =
        let proc = nameOf Project.Proc
        let folder = sprintf "%s/%s" (Paths.Output proc) tfm
        let dll = sprintf "%s/%s.dll" folder proc
        let systemReactive = sprintf "%s/System.Reactive.dll" folder
        let systemReactiveLinq = sprintf "%s/System.Reactive.Linq.dll" folder
        let keyFile = Paths.Keys("keypair.snk");
        let options = 
                [ 
                    "/keyfile:", keyFile;
                    "/internalize", "";
                    "/lib:", folder;
                    "/out:", dll;
                ] 
                |> List.map (fun (p,v) -> sprintf "%s%s" p v)
                    
        let args = [dll; systemReactive; systemReactiveLinq] |> List.append options;
                    
        Tooling.ILRepack.Exec args |> ignore
        rewriteNamespace tfm |> ignore
            
    let ILRepack() = 
        //ilrepack on mono crashes pretty hard on my machine
        match isMono with
        | true -> ignore()
        | false -> 
            ilRepackInternal "netstandard2.0"
            ilRepackInternal "net45"
            ilRepackInternal "net46"
            ignore()
        

