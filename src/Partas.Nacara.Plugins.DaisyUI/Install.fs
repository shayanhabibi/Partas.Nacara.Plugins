namespace Nacara.Plugins.Internal
open Nacara.Plugins
open Nacara.Core
open Nacara.Plugins.TailwindCssBinary

[<RequireQualifiedAccess>]
module DaisyUIInstall =
    type Paths = {| ``daisyui.mjs`` : string; ``daisyui-theme.mjs`` : string |}
    let [<Literal>] private githubOwner = "saadeghi"
    let [<Literal>] private repository = "daisyui"
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

    let private requestFileWith file (version: Version) =
        getVersionDownloadRoot version
        |> Result.map (fun urlRoot ->
            let url = $"{urlRoot}/{file}"
            {
                Version =
                    match version with
                    | Latest -> "latest"
                    | GitHubTag tag -> tag
                    | Version (major, minor, patch) -> $"%i{major}.%i{minor}.%i{patch}"
                    |> sprintf "%s-%s" file
                Name = "daisyui"
                Url = url
                Archive = ToolArchive.Raw
                Files = [ file ]
                Executable = [ ]
                Checksum = None
            })
    let private requestFile file = requestFileWith file Latest

    let resolveWith(version: Version) =
        let daisyFileOne = "daisyui.mjs"
        let daisyFileTwo = "daisyui-theme.mjs"
        requestFileWith daisyFileOne version
        |> Result.bind (Tool.file daisyFileOne)
        |> Result.bind (fun daisyFileOne ->
            requestFileWith daisyFileTwo version
            |> Result.bind (Tool.file daisyFileTwo)
            |> Result.map (fun daisyFileTwo ->
                {| ``daisyui.mjs`` = daisyFileOne
                   ``daisyui-theme.mjs`` = daisyFileTwo |}
                )
            )

    let resolve() = resolveWith Latest

