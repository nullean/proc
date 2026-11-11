/// Locates the NativeAOT-published Proc.Tests.Binary, mirroring tests/Proc.Tests/TestsBase.cs
module Proc.Fs.Tests.TestBinary

open System
open System.IO
open System.Runtime.InteropServices

/// A scratch directory, deleted when disposed
type TempDir() =
    let dir = Directory.CreateTempSubdirectory("proc-fs-tests-")
    member _.Path = dir.FullName
    member _.File name = Path.Combine(dir.FullName, name)
    interface IDisposable with
        member _.Dispose() = try dir.Delete(true) with _ -> ()

let private assemblyDir = Path.GetDirectoryName(typeof<TempDir>.Assembly.Location)

// bin/<configuration>/<tfm>/Proc.Fs.Tests.dll
let private configuration = DirectoryInfo(assemblyDir).Parent.Name
let private tfm = DirectoryInfo(assemblyDir).Name

/// The folder holding the test binary project, used as the working directory of test processes
let workingDir = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "Proc.Tests.Binary"))

/// Absolute path to the test binary for the TFM these tests run under
let path =
    let exe = if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "Proc.Tests.Binary.exe" else "Proc.Tests.Binary"
    let full = Path.Combine(workingDir, "bin", configuration, tfm, RuntimeInformation.RuntimeIdentifier, "publish", exe)
    if not (File.Exists full) then failwithf "Can not find %s" full
    full
