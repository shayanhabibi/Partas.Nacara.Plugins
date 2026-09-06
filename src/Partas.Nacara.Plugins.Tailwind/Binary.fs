namespace Nacara.Plugins

module TailwindCssBinary =
    type Platform =
        /// <summary>Default <c>musl</c> to false if unsure</summary>
        | LinuxArm64 of musl: bool
        /// <summary>Default <c>musl</c> to false if unsure</summary>
        | LinuxX64 of musl: bool
        | MacArm64
        | MacX64
        | WindowsX64
        | Auto
    type Version =
        | Latest
        | Version of major: int * minor: int * patch: int
        | GitHubTag of tag: string
    /// Where to find the TailwindCss binary.
    /// If implicit, then the provided information is used to download the binary if not cached.
    type Strategy =
        | Implicit of version: Version * platform: Platform
        | Explicit of path: string

namespace Nacara.Plugins.Internal
open Nacara.Core
open Nacara.Plugins.TailwindCssBinary

[<RequireQualifiedAccess>]
module TailwindCssBinary =
    let [<Literal>] private githubOwner = "tailwindlabs"
    let [<Literal>] private repository = "tailwindcss"
    let [<Literal>] private githubReleaseUrl = "https://github.com/"+githubOwner+"/"+repository+"/releases"
    let private getVersionDownloadRoot = function
        | Latest ->
            Ok $"{githubReleaseUrl}/latest/download"
        | Version (major, minor, patch) ->
            Ok $"{githubReleaseUrl}/download/v{major}.{minor}.{patch}"
        | GitHubTag tag ->
            if tag.StartsWith("https://") || tag.Contains("/")
            then Error $"Invalid GitHub tag: {tag}. Valid tags should result in a 200 OK when added to the url {githubReleaseUrl}/tag/<tag>"
            else Ok tag
    let private getPlatformDownloadStem =
        function
        | { IsWindows = true } -> Ok "windows-x64.exe"
        | { Architecture = "arm64" } as platform ->
            if platform.IsLinux
            then "linux-arm64"
            else "macos-arm64"
            |> Ok
        | { Architecture = "x64" } as platform ->
            if platform.IsLinux
            then "linux-x64"
            else "macos-x64"
            |> Ok
        | platform -> Error $"Unsupported platform: {platform}"
    let private requestWith (strategy: Strategy) =
        match strategy with
        | Explicit _ -> Error "Explicit strategy is not supported for requests"
        // default
        | Implicit(version, platform) ->
            let versionUrlRoot =
                version
                |> getVersionDownloadRoot
            let platformStem =
                match platform with
                | Auto ->
                    Tool.platform()
                    |> Result.bind getPlatformDownloadStem
                | LinuxArm64 true -> Ok "linux-arm64-musl"
                | LinuxArm64 false -> Ok "linux-arm64"
                | LinuxX64 true -> Ok "linux-x64-musl"
                | LinuxX64 false -> Ok "linux-x64"
                | MacArm64 -> Ok "macos-arm64"
                | MacX64 -> Ok "macos-x64"
                | WindowsX64 -> Ok "windows-x64.exe"
                |> Result.map (sprintf "tailwindcss-%s")
            versionUrlRoot
            |> Result.bind (fun urlRoot ->
                platformStem |> Result.map (sprintf "%s/%s" urlRoot)
                )
            |> Result.map (fun url ->
                {
                    Version =
                        match version with
                        | Latest -> "latest"
                        | GitHubTag tag -> tag
                        | Version (major, minor, patch) -> $"%i{major}.%i{minor}.%i{patch}"
                    Name = "tailwindcss"
                    Url = url
                    Archive = ToolArchive.Raw
                    Files = [ platformStem |> Result.toValueOption |> ValueOption.get ]
                    Executable = [ platformStem |> Result.toValueOption |> ValueOption.get ]
                    Checksum = None
                })

    let private request () = requestWith <| Strategy.Implicit(Version.Version(4,3,3), Auto)

    let resolveWith(strategy: Strategy) =
        requestWith strategy
        |> Result.bind (fun request -> Tool.file (List.head request.Files) request)
    let resolve() = resolveWith <| Strategy.Implicit(Version.Version(4,3,3), Auto)
