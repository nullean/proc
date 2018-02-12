#I @"../../packages/build/FAKE/tools"
#r @"FakeLib.dll"
#r @"System.IO.Compression.FileSystem.dll"

open System
open System.IO
open System.Diagnostics
open System.Net

#load @"Paths.fsx"

open Fake

Fake.ProcessHelper.redirectOutputToTrace <-true
    
module Tooling = 
    open Paths
    open Projects

    let private fileDoesNotExist path = path |> Path.GetFullPath |> File.Exists |> not
    let private dirDoesNotExist path = path |> Path.GetFullPath |> Directory.Exists |> not
    let private doesNotExist path = (fileDoesNotExist path) && (dirDoesNotExist path)

    (* helper functions *)
    #if mono_posix
    #r "Mono.Posix.dll"
    open Mono.Unix.Native
    let private applyExecutionPermissionUnix path =
        let _,stat = Syscall.lstat(path)
        Syscall.chmod(path, FilePermissions.S_IXUSR ||| stat.st_mode) |> ignore
    #else
    let private applyExecutionPermissionUnix path = ()
    #endif

    let private execAt (workingDir:string) (exePath:string) (args:string seq) =
        let processStart (psi:ProcessStartInfo) =
            let ps = Process.Start(psi)
            ps.WaitForExit ()
            ps.ExitCode
        let fullExePath = exePath |> Path.GetFullPath
        applyExecutionPermissionUnix fullExePath
        let exitCode = 
            ProcessStartInfo(
                        fullExePath,
                        args |> String.concat " ",
                        WorkingDirectory = (workingDir |> Path.GetFullPath),
                        UseShellExecute = false) 
                   |> processStart
        if exitCode <> 0 then
            exit exitCode
        ()

    let execProcessWithTimeout proc arguments timeout workingDir = 
        let args = arguments |> String.concat " "
        ExecProcess (fun info ->
            info.FileName <- proc
            info.WorkingDirectory <- workingDir
            info.Arguments <- args
        ) timeout

    let execProcessWithTimeoutAndReturnMessages proc arguments timeout = 
        let args = arguments |> String.concat " "
        let code = 
            ExecProcessAndReturnMessages (fun info ->
            info.FileName <- proc
            info.WorkingDirectory <- "."
            info.Arguments <- args
            ) timeout
        code

    let private defaultTimeout = TimeSpan.FromMinutes 15.0

    let execProcessInDirectory proc arguments workingDir =
        let exitCode = execProcessWithTimeout proc arguments defaultTimeout workingDir
        match exitCode with
        | 0 -> exitCode
        | _ -> failwithf "Calling %s resulted in unexpected exitCode %i" proc exitCode 

    let execProcess proc arguments = execProcessInDirectory proc arguments "."

    let execProcessAndReturnMessages proc arguments =
        execProcessWithTimeoutAndReturnMessages proc arguments defaultTimeout

    type BuildTooling(path) =
        member this.Path = path
        member this.Exec arguments = execProcess this.Path arguments
        member this.ExecIn workingDirectory arguments = execProcessInDirectory this.Path arguments workingDirectory

    let XUnit = new BuildTooling(Paths.Tool("xunit.runner.console/tools/xunit.console.exe"))

    type DotNetTooling(exe) =
       member this.Exec arguments =
            this.ExecWithTimeout arguments (TimeSpan.FromMinutes 30.)

        member this.ExecWithTimeout arguments timeout =
            let result = execProcessWithTimeout exe arguments timeout "."
            if result <> 0 then failwith (sprintf "Failed to run dotnet tooling for %s args: %A" exe arguments)

    let DotNet = new DotNetTooling("dotnet.exe")


