#r "paket: groupref fake-build //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.BuildServer
open Fake.DotNet
open Fake.DotNet.Testing
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open System
open System.IO

// [x] clean
// [x] assemblyinfo
// [x] build
// [x] test
// [ ] docs (gh-pages)
// [ ] package (nuget)
// [ ] release (myget, chocolatey)

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Action BI Toolkit | pbi-tools"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "The Power BI developer's toolkit."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "A command-line tool to work with Power BI Desktop files. Enables change tracking for PBIx files in source control as well as generating those files programmatically."

// List of author names (for NuGet package)
let authors = [ "Mathias Thierbach" ]
let company = "Action BI Toolkit Ltd"

// Tags for your project (for NuGet package)
let tags = "powerbi, pbix, source-control, automation"

// File system information
let solutionFile  = "PBI-TOOLS.sln"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "action-bi-toolkit"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "pbi-tools"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" ("https://raw.github.com/" + gitOwner)

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

BuildServer.install [
    TeamFoundation.Installer  // Adds support for Azure DevOps
]

let buildDir = ".build"
let outDir = buildDir @@ "out"
let distDir = buildDir @@ "dist"
let distFullDir = distDir @@ "desktop"
let distCoreDir = distDir @@ "core"
let testDir = buildDir @@ "test"
let tempDir = ".temp"

// Pattern specifying assemblies to be tested using xUnit
let testAssemblies = outDir @@ "*Tests*.dll"

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

// Read additional information from the release notes document
let releaseNotesData = 
    File.ReadAllLines "RELEASE_NOTES.md"
    |> ReleaseNotes.parseAll

let release = List.head releaseNotesData
let assemblyVersion = sprintf "%i.%i.0.0" release.SemVer.Major release.SemVer.Minor
let timestampString =
    let now = DateTime.UtcNow
    let ytd = now - DateTime(now.Year,1,1,0,0,0,DateTimeKind.Utc)
    String.Format("{0:yy}{1:ddd}.{0:HHmm}",now,ytd)
let fileVersion = sprintf "%i.%i.%s"
                   release.SemVer.Major
                   release.SemVer.Minor
                   timestampString

Trace.logfn "Current Release:\n%O" release

let stable = 
    match releaseNotesData |> List.tryFind (fun r -> r.NugetVersion.Contains("-") |> not) with
    | Some stable -> stable
    | _ -> release

/// If 'PBITOOLS_PbiInstallDir' points to a valid PBI Desktop installation, set the MSBuild 'ReferencePath' property to that location
let pbiInstallDir =
    lazy ( match Environment.environVarOrNone "PBITOOLS_PbiInstallDir" with // PBIDesktop.exe might be in a sub-folder .. getting that folder here
           | Some path -> Directory.EnumerateFiles(path, "PBIDesktop.exe", SearchOption.AllDirectories)
                         |> Seq.tryHead
                         |> function
                            | Some p -> Some (p |> Path.getDirectory)
                            | _ -> None
           | None -> None )

let pbiBuildVersion =
    lazy ( let dir = match pbiInstallDir.Value with
                     | Some p -> p
                     | _ -> @"C:\Program Files\Microsoft Power BI Desktop\bin" // this path is hard-coded in the csproj, so fine to do the same here
           let pbiDesktopPath = Path.combine dir "PBIDesktop.exe"
           if pbiDesktopPath |> File.exists then
              pbiDesktopPath |> File.tryGetVersion
           else
              None )

let genCSAssemblyInfo (projectPath : string) =
    let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
    let folderName = System.IO.Path.GetDirectoryName(projectPath)
    let basePath = folderName @@ "Properties"
    let fileName = basePath @@ "AssemblyInfo.cs"
    AssemblyInfoFile.createCSharp fileName
      [ AssemblyInfo.Title (projectName)
        AssemblyInfo.Product project
        AssemblyInfo.Company company
        AssemblyInfo.Copyright (sprintf "Copyright \u00A9 Mathias Thierbach 2018-%i" (let today = DateTime.Today in today.Year)) // Avoids warning FS0052, see: https://github.com/fsharp/FAKE/issues/1803
        AssemblyInfo.Description summary
        AssemblyInfo.Version release.AssemblyVersion
        AssemblyInfo.FileVersion fileVersion
        AssemblyInfo.InformationalVersion release.NugetVersion
        AssemblyInfo.Metadata ("PBIBuildVersion", match pbiBuildVersion.Value with | Some v -> v | _ -> "" ) ]

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    !! "src/**/*.csproj"
    |> Seq.filter (fun s -> not <| s.Contains("PbiDownloader"))
    |> Seq.iter genCSAssemblyInfo
)



// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    !! "src/*/bin"
    ++ "tests/*/bin"
    ++ "src/*/obj"
    ++ "tests/*/obj"
    ++ buildDir 
    ++ tempDir
    |> Shell.cleanDirs
)


// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "ZipSampleData" (fun _ ->
    !! "data/Samples/Adventure Works DW 2020/**/*.*"
    |> Zip.zip "data/Samples/Adventure Works DW 2020" (tempDir @@ "Adventure Works DW 2020.zip")
)


// Including 'Restore' target addresses issue: https://github.com/fsprojects/Paket/issues/2697
// Previously, msbuild would fail not being able to find **\obj\project.assets.json
Target.create "Build" (fun _ ->
    let msbuildProps = match pbiInstallDir.Value with
                       | Some dir -> Trace.logfn "Using assembly ReferencePath: %s" dir
                                     [ "ReferencePath", dir ]
                       | _ -> []
    // let setParams (defaults:MSBuildParams) =
    //     { defaults with
    //         MaxCpuCount = None 
    //     }

    !! solutionFile
    |> MSBuild.runReleaseExt id null msbuildProps "Restore;Rebuild"
    |> ignore
)


Target.create "Publish" (fun _ -> 
    let msbuildProps = match pbiInstallDir.Value with
                       | Some dir -> Trace.logfn "Using assembly ReferencePath: %s" dir
                                     [ "ReferencePath", dir ]
                       | _ -> []

    let setParams (rid, path) =
        fun (args : DotNet.PublishOptions) -> 
            { args with
                Runtime = Some rid
                SelfContained = Some false
                //NoRestore = true
                OutputPath = Some path
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = { args.MSBuildParams with
                                       Properties = msbuildProps }
            }
    
    // Desktop build
    DotNet.publish
        (setParams ("win10-x64", distFullDir)) 
        "src/PBI-Tools/PBI-Tools.csproj"
    
    // Core build
    [ "win10-x64",      "win-x64"
      "linux-x64",      "linux-x64"
      "linux-musl-x64", "alpine-x64" ]
    |> Seq.iter (fun (rid, path) ->
        DotNet.publish 
            (setParams (rid, distCoreDir @@ path)) 
            "src/PBI-Tools.NETCore/PBI-Tools.NETCore.csproj"
    )
)


Target.create "Pack" (fun _ ->
    !! (distFullDir @@ "*.*")
    |> Zip.zip distFullDir (sprintf @"%s\pbi-tools.%s.zip" buildDir release.NugetVersion)

    distCoreDir
    |> Directory.EnumerateDirectories
    |> Seq.map (Path.GetFileName) 
    |> Seq.iter (fun dist ->
        !! (distCoreDir @@ dist @@ "*.*")
        |> Zip.zip (distCoreDir @@ dist) (sprintf @"%s\pbi-tools.core.%s_%s.zip" buildDir release.NugetVersion dist)
    )
)


Target.create "Test" (fun _ ->
    !! "tests/*/bin/Release/**/pbi-tools*tests.dll"
    -- "tests/*/bin/Release/**/*netcore*.dll"
    |> XUnit2.run (fun p -> { p with HtmlOutputPath = Some (testDir @@ "xunit.html")
                                     XmlOutputPath = Some (testDir @@ "xunit.xml")
                                     ToolPath = "packages/fake-tools/xunit.runner.console/tools/net472/xunit.console.exe" } )
)


Target.create "SmokeTest" (fun _ ->
    // Copy all *.pbix from /data folder to TEMP
    // Run 'pbi-tools extract' on all
    // Fail if error code is returned

    !! "data/**/*.pbix"
    -- "data/external/**"
    |> Shell.copyFilesWithSubFolder tempDir

    // 'external' folder contains files with deeply nested folder structures,
    // likely to hit the Windows 260-character limit for paths
    // copying those without sub folders to keep extracted paths shorter
    !! "data/external/**/*.pbix"
    |> Shell.copyFiles tempDir

    !! (tempDir @@ "**/*.pbix")
    |> Seq.iter (fun path ->
        [ "extract"; path ]
        |> CreateProcess.fromRawCommand (distFullDir @@ "pbi-tools.exe")
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore
    )
)


Target.create "UsageDocs" (fun _ ->
    [ "export-usage"; "-outPath"; "./docs/Usage.md" ]
    |> CreateProcess.fromRawCommand (distFullDir @@ "pbi-tools.exe")
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore
)


Target.create "Help" (fun _ ->
    Trace.traceError "Please specify a target to run."
)

// --------------------------------------------------------------------------------------

open Fake.Core.TargetOperators

"Clean"
  ==> "AssemblyInfo"
  ==> "ZipSampleData"
  ==> "Build"
  ==> "Test"

"Build"
  ==> "SmokeTest"

"Build"
  ==> "Publish"

"Publish"
  ==> "Test"
  ==> "UsageDocs"
  ==> "Pack"

// --------------------------------------------------------------------------------------
// Show help by default. Invoke 'fake build -t <Target>' to override

Target.runOrDefaultWithArguments "Help"
