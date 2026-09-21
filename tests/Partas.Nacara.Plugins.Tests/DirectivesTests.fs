namespace Partas.Nacara.Plugins.Tests

open Expecto
open Nacara.Plugins

module DirectivesTests =
    [<Tests>]
    let tests =
        testList "Arguments" [
            test "toFlowMapping returns empty JSON object" {
                let result = Arguments.toFlowMapping ""
                Expect.equal result (Ok "{}") "Should return empty JSON object"
            }
        ]
