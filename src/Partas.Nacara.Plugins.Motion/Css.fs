namespace Nacara.Plugins

open System
open System.Globalization

/// <summary>What the generated stylesheet does for visitors who ask for reduced motion.</summary>
type ReducedMotion =
    /// <summary>Hovers still change the element, but at once.</summary>
    | Instant
    /// <summary>Hovers play as authored.</summary>
    | Keep
    /// <summary>Hovers do not apply at all.</summary>
    | Off

/// <summary>Hovers compiled to CSS transitions.</summary>
module MotionCss =

    let private number (x: float) =
        (Math.Round(x, 3, MidpointRounding.AwayFromZero) + 0.0).ToString("0.###", CultureInfo.InvariantCulture)

    /// <summary>The CSS value: a token as <c>var(--first-last)</c>, a literal as written.</summary>
    let value (value: MotionValue) : string =
        match value with
        | Literal css -> css
        | Token path ->
            let segments = path.Split '.'
            if segments.Length = 1 then $"var(--%s{path})" else $"var(--%s{segments[0]}-%s{segments[segments.Length - 1]})"

    /// <summary>The CSS easing: <c>linear</c>, <c>cubic-bezier()</c> or a spring's <c>linear()</c>.</summary>
    let easing (easing: Easing) : string =
        match easing with
        | Linear -> "linear"
        | CubicBezier(x1, y1, x2, y2) -> $"cubic-bezier(%s{number x1}, %s{number y1}, %s{number x2}, %s{number y2})"
        | Easing.Spring parameters -> Spring.linear parameters
        | SpringPreset name ->
            match Spring.preset name with
            | Some parameters -> Spring.linear parameters
            | None -> "linear"

    /// <summary>The milliseconds a hover plays over.</summary>
    /// <remarks>A cubic or linear easing plays over its <c>DurationMs</c>, a spring over its settling time and a preset over <c>1200</c>.</remarks>
    let duration (hover: Hover) : int =
        match hover.Easing with
        | Linear
        | CubicBezier _ -> hover.DurationMs
        | Easing.Spring parameters -> Spring.settle parameters
        | SpringPreset _ -> 1200

    // A comma inside parentheses or brackets belongs to one selector.
    let private hasTopLevelComma (selector: string) =
        let mutable depth = 0
        let mutable found = false

        for c in selector do
            match c with
            | '('
            | '[' -> depth <- depth + 1
            | ')'
            | ']' -> depth <- depth - 1
            | ',' when depth = 0 -> found <- true
            | _ -> ()

        found

    let private wrap (selector: string) =
        if hasTopLevelComma selector then $":is(%s{selector})" else selector

    let private transition (hover: Hover) (changes: Change list) =
        let timing = $"%d{duration hover}ms %s{easing hover.Easing}"

        changes
        |> List.map (fun change -> change.Property)
        |> List.distinct
        |> List.sort
        |> List.map (fun property -> $"%s{property} %s{timing}")
        |> String.concat ", "

    let private declarations (changes: Change list) =
        changes
        |> List.sortBy (fun change -> change.Property)
        |> List.map (fun change -> $"%s{change.Property}: %s{value change.To};")
        |> String.concat " "

    // (resting selector, hovered selector list, changes) per element of the hover.
    let private elements (hover: Hover) =
        let selector = wrap hover.Selector

        hover.Changes
        |> List.groupBy (fun change -> change.Part)
        |> List.sortBy (fun (part, _) -> part.IsSome, part)
        |> List.map (fun (part, changes) ->
            match part with
            | None -> selector, $"%s{selector}:hover, %s{selector}:focus-visible", changes
            | Some part -> $"%s{selector} %s{part}", $"%s{selector}:hover %s{part}, %s{selector}:focus-visible %s{part}", changes)

    let private restingRule (hover: Hover) (resting: string, _, changes) =
        resting + " { transition: " + transition hover changes + "; }"

    let private hoveredRule (_, hovered: string, changes) =
        hovered + " { " + declarations changes + " }"

    let private noPreference (rule: string) =
        "@media (prefers-reduced-motion: no-preference) {\n" + rule + "\n}"

    let private restingRules (hover: Hover) =
        elements hover |> List.map (restingRule hover)

    let private hoveredRules (hover: Hover) =
        elements hover |> List.map hoveredRule

    /// <summary>The rules for one hover.</summary>
    let hover (hover: Hover) : string =
        String.concat "\n" (restingRules hover @ hoveredRules hover)

    /// <summary>The stylesheet for the hovers, under a reduced-motion policy.</summary>
    let stylesheet (policy: ReducedMotion) (hovers: Hover list) : string =
        let rules =
            match policy with
            | Off -> hovers |> List.collect (fun h -> restingRules h @ (hoveredRules h |> List.map noPreference))
            | _ -> hovers |> List.collect (fun h -> restingRules h @ hoveredRules h)

        let instant =
            match policy with
            | Instant ->
                let selectors =
                    hovers
                    |> List.collect (fun h -> elements h |> List.map (fun (resting, _, _) -> resting))
                    |> List.distinct

                if selectors.IsEmpty then
                    []
                else
                    [ "@media (prefers-reduced-motion: reduce) {\n"
                      + String.concat ", " selectors
                      + " { transition-duration: 0s; }\n}" ]
            | _ -> []

        String.concat "\n" (rules @ instant)
