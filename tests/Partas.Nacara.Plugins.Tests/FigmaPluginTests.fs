module Partas.Nacara.Plugins.Tests.FigmaPlugin

open System.Diagnostics
open System.IO
open Expecto
open Nacara.Plugins

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

/// Runs node from the repository root, returning its exit code, stdout and stderr.
let private node (args: string list) =
    let info = ProcessStartInfo("node", WorkingDirectory = repoRoot)
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    info.UseShellExecute <- false

    for arg in args do
        info.ArgumentList.Add arg

    use proc = Process.Start info
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()
    proc.ExitCode, stdout.Result, stderr.Result

[<Tests>]
let tests =
    testList
        "figma plugin"
        [
            test "plugin tests pass under node" {
                let code, stdout, stderr = node [ "--test"; "design/figma-plugin/motion.test.js" ]
                Expect.equal code 0 $"node --test failed:\n%s{stdout}\n%s{stderr}"
            }

            test "the plugin's document parses" {
                let code, stdout, stderr = node [ "design/figma-plugin/motion-fixture.js" ]
                Expect.equal code 0 $"node failed:\n%s{stderr}"

                match MotionDocument.parse stdout with
                | Ok document -> Expect.equal document.Hovers.Length 4 "hovers"
                | Error message -> failtest $"%s{message}\n%s{stdout}"
            }
        ]
