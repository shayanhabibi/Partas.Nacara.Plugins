namespace Nacara.Plugins

open System.Text.Json

/// <summary>A value a hover animates between: a design token or a literal CSS value.</summary>
type MotionValue =
    /// <summary>A design token, by its dotted path.</summary>
    | Token of path: string
    /// <summary>A literal CSS value.</summary>
    | Literal of css: string

/// <summary>The physical parameters of a spring.</summary>
type SpringParameters =
    {
        /// <summary>The mass of the spring.</summary>
        Mass: float
        /// <summary>The stiffness of the spring.</summary>
        Stiffness: float
        /// <summary>The damping of the spring.</summary>
        Damping: float
    }

/// <summary>The easing of a hover.</summary>
type Easing =
    /// <summary>No easing.</summary>
    | Linear
    /// <summary>A cubic bezier through the two control points.</summary>
    | CubicBezier of x1: float * y1: float * x2: float * y2: float
    /// <summary>A spring with explicit parameters.</summary>
    | Spring of SpringParameters
    /// <summary>One of Figma's named springs: <c>GENTLE</c>, <c>QUICK</c>, <c>BOUNCY</c> or <c>SLOW</c>.</summary>
    | SpringPreset of name: string

/// <summary>One property of one element changing during a hover.</summary>
type Change =
    {
        /// <summary>The Figma layer name; absent for the root element.</summary>
        Layer: string option
        /// <summary>The CSS selector of a child below the hover's selector; absent for the root element.</summary>
        Part: string option
        /// <summary>The CSS property.</summary>
        Property: string
        /// <summary>The value at rest.</summary>
        From: MotionValue
        /// <summary>The value while hovered.</summary>
        To: MotionValue
    }

/// <summary>A difference between the two states that the document does not translate.</summary>
type NotTranslated =
    {
        /// <summary>The Figma layer name, when it concerns a layer.</summary>
        Layer: string option
        /// <summary>The property.</summary>
        Property: string
        /// <summary>Why it is not translated.</summary>
        Reason: string
    }

/// <summary>A hover: where it applies, how long it plays and what it changes.</summary>
type Hover =
    {
        /// <summary>The hover's name.</summary>
        Name: string
        /// <summary>The CSS selector the hover applies to.</summary>
        Selector: string
        /// <summary>The trigger; <c>hover</c>.</summary>
        Trigger: string
        /// <summary>The duration in milliseconds.</summary>
        DurationMs: int
        /// <summary>The easing.</summary>
        Easing: Easing
        /// <summary>The properties that change.</summary>
        Changes: Change list
        /// <summary>The differences that are reported, not translated.</summary>
        NotTranslated: NotTranslated list
    }

/// <summary>A variant the exporter skipped.</summary>
type Skipped =
    {
        /// <summary>The component.</summary>
        Component: string
        /// <summary>The variant.</summary>
        Variant: string
        /// <summary>Why it was skipped.</summary>
        Reason: string
    }

/// <summary>The motion document the Figma plugin exports.</summary>
type MotionDocument =
    {
        /// <summary>The document format version.</summary>
        Version: int
        /// <summary>The hovers.</summary>
        Hovers: Hover list
        /// <summary>The variants that were skipped.</summary>
        Skipped: Skipped list
    }

/// <summary>Reading the motion document.</summary>
module MotionDocument =

    /// <summary>The document version this package reads.</summary>
    [<Literal>]
    let SupportedVersion = 1

    let private namedCurves =
        dict
            [
                "EASE_IN", (0.42, 0.0, 1.0, 1.0)
                "EASE_OUT", (0.0, 0.0, 0.58, 1.0)
                "EASE_IN_AND_OUT", (0.42, 0.0, 0.58, 1.0)
                "EASE_IN_BACK", (0.3, -0.05, 0.7, -0.5)
                "EASE_OUT_BACK", (0.45, 1.45, 0.8, 1.0)
                "EASE_IN_AND_OUT_BACK", (0.7, -0.4, 0.4, 1.4)
            ]

    let private presets = set [ "GENTLE"; "QUICK"; "BOUNCY"; "SLOW" ]

    let private tryProp (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
        | _ -> None

    let private str name element =
        tryProp name element |> Option.map (fun value -> value.GetString())

    let private required name (element: JsonElement) =
        match tryProp name element with
        | Some value -> value
        | None -> failwith $"The motion document is missing \"%s{name}\"."

    let private list name (element: JsonElement) =
        match tryProp name element with
        | Some value -> [ for item in value.EnumerateArray() -> item ]
        | None -> []

    let private requiredString name element = (required name element).GetString()

    let private number name element = (required name element).GetDouble()

    let private value (element: JsonElement) =
        match str "token" element, str "value" element with
        | Some path, _ -> Token path
        | None, Some css -> Literal css
        | None, None -> failwith "A motion value needs \"token\" or \"value\"."

    let private easing hover (element: JsonElement) =
        match requiredString "type" element with
        | "LINEAR" -> Linear
        | "CUSTOM_CUBIC_BEZIER" ->
            CubicBezier(number "x1" element, number "y1" element, number "x2" element, number "y2" element)
        | "CUSTOM_SPRING" ->
            Spring
                {
                    Mass = number "mass" element
                    Stiffness = number "stiffness" element
                    Damping = number "damping" element
                }
        | name when presets.Contains name -> SpringPreset name
        | name when namedCurves.ContainsKey name ->
            let x1, y1, x2, y2 = namedCurves[name]
            CubicBezier(x1, y1, x2, y2)
        | other -> failwith $"The hover \"%s{hover}\" has an unknown easing type \"%s{other}\"."

    let private change (element: JsonElement) =
        {
            Layer = str "layer" element
            Part = str "part" element
            Property = requiredString "property" element
            From = value (required "from" element)
            To = value (required "to" element)
        }

    let private notTranslated (element: JsonElement) =
        {
            Layer = str "layer" element
            Property = requiredString "property" element
            Reason = requiredString "reason" element
        }

    let private hover (element: JsonElement) =
        let name = requiredString "name" element

        {
            Name = name
            Selector = requiredString "selector" element
            Trigger = str "trigger" element |> Option.defaultValue "hover"
            DurationMs = number "duration" element |> System.Math.Round |> int
            Easing = easing name (required "easing" element)
            Changes = list "changes" element |> List.map change
            NotTranslated = list "notTranslated" element |> List.map notTranslated
        }

    let private skipped (element: JsonElement) =
        {
            Component = requiredString "component" element
            Variant = requiredString "variant" element
            Reason = requiredString "reason" element
        }

    /// <summary>Reads a motion document, or says why it cannot.</summary>
    let parse (json: string) : Result<MotionDocument, string> =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            let version = (required "version" root).GetInt32()

            if version <> SupportedVersion then
                Error $"The motion document is version %d{version}; this package reads version %d{SupportedVersion}."
            else
                Ok
                    {
                        Version = version
                        Hovers = list "hovers" root |> List.map hover
                        Skipped = list "skipped" root |> List.map skipped
                    }
        with
        | :? JsonException as error -> Error $"The motion document is not valid JSON: %s{error.Message}"
        | Failure message -> Error message
