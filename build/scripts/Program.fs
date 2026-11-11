module Program

open System
open System.IO
open Nullean.Make.Fs
open Proc.Fs

/// The build's targets; `dotnet run --project build/scripts -- --help` lists them with these descriptions
type Target =
    /// clean known output locations
    | Clean
    /// Run build
    | Build
    /// Runs build then tests
    | Test
    /// Runs build, then creates and validates the packages and reports public API changes
    | Pack
    /// Runs tests, then creates and validates the packages: exactly what CI runs
    | Ci
    /// Pack the NuGet packages into build/output
    | Generatepackages
    /// Validate the packed NuGet packages
    | Validatepackages
    /// Report public API changes against the previous release
    | Generateapichanges

/// build/output, relative to the repository root
let private output = Paths.RootRelative Paths.Output.FullName

/// The packed .nupkg files, newest first
let private packages () =
    Paths.Output.GetFiles("*.nupkg") |> Seq.sortByDescending (fun f -> f.CreationTimeUtc) |> Seq.toList

/// Every project but this build itself: `dotnet clean` on the solution would delete the assemblies the running
/// build still loads lazily and fail the next target
let private projectsToClean () =
    ["src"; "tests"; "examples"]
    |> List.collect (fun d -> Directory.EnumerateFiles(d, "*.?sproj", SearchOption.AllDirectories) |> Seq.toList)

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("dotnet run --project build/scripts --", None)

    // `-s` skips what a target `requires`, never what it `composes`: build always cleans first
    app.Bind <| function
        | Clean -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              for project in projectsToClean () do
                  sh $"dotnet clean {project} -c Release -v q" }
        | Build -> command {
              composes [Clean]
              executes (shell { sh $"dotnet build -c Release" }) }
        // global.json selects Microsoft.Testing.Platform, so this runs the TUnit test executables directly; on GitHub
        // Actions TUnit writes its own summary of the run to GITHUB_STEP_SUMMARY
        | Test -> target [Build] { sh $"dotnet test -c Release" }
        | Pack -> command { requires [Build]; composes [Generatepackages; Validatepackages; Generateapichanges] }
        | Ci -> command { requires [Test]; composes [Generatepackages; Validatepackages; Generateapichanges] }
        | Generatepackages -> target [] { sh $"dotnet pack -c Release -o {output}" }
        // nupkg-validator 0.11+ ships per-RID NativeAOT packages, so it validates net11.0 dlls without needing a
        // matching runtime. Its RID packages (nupkg-validator.linux-x64, ...) only live on nuget.org, not on the GitHub
        // Packages feed CI configures, so it runs through `dotnet tool exec` pinned to nuget.org rather than through the
        // tool manifest.
        | Validatepackages -> target [] {
              let version = Versions.informational.Value
              let jenkinsOnWindows =
                  not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "JENKINS_URL")) && OperatingSystem.IsWindows()
              for package in packages () do
                  let package = Paths.RootRelative package.FullName
                  if jenkinsOnWindows then
                      sh $"dotnet tool exec nupkg-validator@0.11.4 --source https://api.nuget.org/v3/index.json -- validate {package} -v {version} -k {Paths.SignKey} -t {output} -r"
                  else
                      sh $"dotnet tool exec nupkg-validator@0.11.4 --source https://api.nuget.org/v3/index.json -- validate {package} -v {version} -k {Paths.SignKey} -t {output}" }
        | Generateapichanges -> target [] {
              let version = Versions.current.Value
              for package in packages () do
                  let id = Path.GetFileNameWithoutExtension(package.Name).Replace("." + version, "")
                  let report = Path.Combine(output, $"breaking-changes-{id}.md")
                  // Proc.Fs only targets modern .NET: compare its net10.0 build against the previous netstandard2.0 package
                  let tfm = if Directory.Exists $"src/{id}/bin/Release/{Paths.MainTFM}" then Paths.MainTFM else "net10.0"
                  // quoted: an unquoted | would pipe
                  sh $"dotnet assembly-differ 'previous-nuget|{id}|{version}|{Paths.MainTFM}' 'directory|src/{id}/bin/Release/{tfm}' -a true --target {id} -f github-comment --output {report}" }

    app.RunAsync(argv).GetAwaiter().GetResult()
