// Copyright 2026 Shayan Habibi.
// Part of Partas.Nacara.Theme, and licensed with it under the Apache License, Version 2.0.
// See LICENSE and NOTICE at the root of this repository.

namespace Partas.Nacara.Theme

open System
open System.Globalization
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Microsoft.FSharp.Reflection

/// <summary>
/// The theme's tokens as a <a href="https://www.designtokens.org/tr/drafts/format/">design
/// tokens</a> document, the JSON that Figma variables and other design tools read and write.
/// </summary>
/// <remarks>
/// <para>
/// One document holds one colour scheme. A token's path is its prefix, its kind and its CSS
/// name, so <c>--nacara-bg-subtle</c> is <c>nacara.color.bg-subtle</c> and
/// <c>--tok-keyword</c> is <c>tok.color.keyword</c>. The kinds are <c>color</c> (a hex
/// colour), <c>size</c> (a length in <c>px</c> or <c>rem</c>), <c>number</c> (a bare number:
/// a line height, or a <c>fontWeight</c>), <c>shadow</c> (a <c>box-shadow</c> of lengths and
/// one colour per layer), <c>font</c> (a font stack) and <c>css</c> (anything else, kept as
/// written). A token whose value is <c>var()</c> of another
/// token is an alias to it, and takes that token's kind.
/// </para>
/// <para>
/// Reading goes by the last segment of a path alone, so a design tool regrouping tokens does
/// not lose them.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module DesignTokens =

    /// <summary>What a token's value is, which decides its group and its JSON shape.</summary>
    /// <summary>One layer of a <c>box-shadow</c>: its lengths in CSS order, then its colour.</summary>
    type private ShadowLayer =
        {
            Inset: bool
            Lengths: (decimal * string) list
            Color: string
        }

    type private Kind =
        | Color
        | Size of value: decimal * unit: string
        | Number of value: decimal
        | Shadow of layers: ShadowLayer list
        | Font
        | Css

    let private hex = Regex(@"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")

    let private length = Regex(@"^(-?\d*\.?\d+)(px|rem)$")

    let private plainNumber = Regex(@"^-?\d*\.?\d+$")

    let private reference = Regex(@"^var\(\s*--([a-z]+)-([a-z0-9-]+)\s*\)$")

    let private decimalOf (text: string) =
        Decimal.Parse(text, CultureInfo.InvariantCulture)

    /// <summary>Splits on a separator outside parentheses, so <c>rgb(0 0 0)</c> stays whole.</summary>
    let private splitTop (separator: char -> bool) (text: string) =
        let parts = ResizeArray<string>()
        let current = Text.StringBuilder()
        let mutable depth = 0

        for character in text do
            match character with
            | '(' ->
                depth <- depth + 1
                current.Append character |> ignore
            | ')' ->
                depth <- depth - 1
                current.Append character |> ignore
            | _ when depth = 0 && separator character ->
                parts.Add(current.ToString())
                current.Clear() |> ignore
            | _ -> current.Append character |> ignore

        parts.Add(current.ToString())
        parts |> Seq.map _.Trim() |> Seq.filter (String.IsNullOrEmpty >> not) |> List.ofSeq

    let private channel (value: float) =
        (int (Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0))).ToString("x2")

    let private rgbFunction = Regex(@"^rgba?\((.*)\)$")

    /// <summary>A CSS colour a design tool can hold, as hex: hex already, or <c>rgb()</c>.</summary>
    let private hexColor (text: string) =
        let functional = rgbFunction.Match text

        if hex.IsMatch text then
            Some(text.ToLowerInvariant())
        elif functional.Success then
            let part (value: string) (scale: float) =
                if value.EndsWith "%" then
                    Double.Parse(value.TrimEnd '%', CultureInfo.InvariantCulture) / 100.0
                else
                    Double.Parse(value, CultureInfo.InvariantCulture) / scale

            match Regex.Split(functional.Groups[1].Value.Trim(), @"\s*[\s,/]\s*") with
            | [| r; g; b |] -> Some $"#%s{channel (part r 255.0)}%s{channel (part g 255.0)}%s{channel (part b 255.0)}"
            | [| r; g; b; a |] ->
                let alpha = part a 1.0
                let suffix = if alpha < 1.0 then channel alpha else ""
                Some $"#%s{channel (part r 255.0)}%s{channel (part g 255.0)}%s{channel (part b 255.0)}%s{suffix}"
            | _ -> None
        else
            None

    /// <summary>A <c>box-shadow</c> value's layers, or nothing when it is not one a design tool can draw.</summary>
    let private shadowLayers (value: string) =
        let layer (text: string) =
            let parts = splitTop Char.IsWhiteSpace text
            let inset = parts |> List.contains "inset"
            let parts = parts |> List.filter ((<>) "inset")

            let lengths =
                parts
                |> List.choose (fun part ->
                    let measured = length.Match part

                    if part = "0" then Some(0m, "px")
                    elif measured.Success then Some(decimalOf measured.Groups[1].Value, measured.Groups[2].Value)
                    else None
                )

            match parts |> List.filter (fun part -> part <> "0" && not (length.IsMatch part)) with
            | [ color ] when lengths.Length >= 2 && lengths.Length <= 4 ->
                hexColor color |> Option.map (fun color -> { Inset = inset; Lengths = lengths; Color = color })
            | _ -> None

        let layers = splitTop ((=) ',') value |> List.map layer

        if not layers.IsEmpty && layers |> List.forall Option.isSome then
            Some(layers |> List.choose id)
        else
            None

    let private fields (record: obj) =
        FSharpType.GetRecordFields(record.GetType())
        |> Array.map (fun field -> Tokens.kebab field.Name, string (field.GetValue record))

    let rec private kindOf (siblings: Map<string, string>) (name: string) (value: string) =
        let value = value.Trim()
        let referenced = reference.Match value
        let measured = length.Match value

        if referenced.Success then
            match siblings |> Map.tryFind referenced.Groups[2].Value with
            | Some target when target <> value -> kindOf siblings referenced.Groups[2].Value target
            | _ -> Css
        elif hex.IsMatch value then
            Color
        elif measured.Success then
            Size(decimalOf measured.Groups[1].Value, measured.Groups[2].Value)
        elif plainNumber.IsMatch value then
            Number(decimalOf value)
        else
            match shadowLayers value with
            | Some layers when name.StartsWith "shadow" -> Shadow layers
            | _ when name.StartsWith "font-" -> Font
            | _ -> Css

    let private group =
        function
        | Color -> "color"
        | Size _ -> "size"
        | Number _ -> "number"
        | Shadow _ -> "shadow"
        | Font -> "font"
        | Css -> "css"

    let private tokenType (name: string) =
        function
        | Color -> "color"
        | Size _ -> "dimension"
        | Number _ when name.StartsWith "font-weight" -> "fontWeight"
        | Number _ -> "number"
        | Shadow _ -> "shadow"
        | Font -> "fontFamily"
        | Css -> "string"

    let private dimension (amount: decimal, unit: string) =
        let written = JsonObject()
        written["value"] <- JsonValue.Create amount
        written["unit"] <- JsonValue.Create unit
        written

    /// <summary>A shadow as the design tokens format writes one: a list of layers.</summary>
    let private shadowJson (layers: ShadowLayer list) =
        let written = JsonArray()

        for layer in layers do
            let entry = JsonObject()
            entry["color"] <- JsonValue.Create layer.Color

            let zero = (0m, "px")

            match layer.Lengths @ List.replicate (4 - layer.Lengths.Length) zero with
            | [ x; y; blur; spread ] ->
                entry["offsetX"] <- dimension x
                entry["offsetY"] <- dimension y
                entry["blur"] <- dimension blur
                entry["spread"] <- dimension spread
            | _ -> ()

            if layer.Inset then
                entry["inset"] <- JsonValue.Create true

            written.Add entry

        written

    let private section (prefix: string) (record: obj) =
        let pairs = fields record
        let siblings = Map.ofArray pairs
        let root = JsonObject()

        for name, value in pairs do
            let kind = kindOf siblings name value
            let referenced = reference.Match(value.Trim())

            let written: JsonNode =
                if referenced.Success && siblings.ContainsKey referenced.Groups[2].Value then
                    let target = referenced.Groups[2].Value
                    let targetKind = kindOf siblings target siblings[target]
                    JsonValue.Create $"{{%s{referenced.Groups[1].Value}.%s{group targetKind}.%s{target}}}"
                else
                    match kind with
                    | Size(amount, unit) -> dimension (amount, unit)
                    | Number amount -> JsonValue.Create amount
                    | Shadow layers -> shadowJson layers
                    | _ -> JsonValue.Create value

            let token = JsonObject()
            token["$type"] <- JsonValue.Create(tokenType name kind)
            token["$value"] <- written

            match root[group kind] with
            | :? JsonObject as existing -> existing[name] <- token
            | _ ->
                let added = JsonObject()
                added[name] <- token
                root[group kind] <- added

        prefix, root

    /// <summary>One colour scheme's tokens as a design tokens document.</summary>
    /// <remarks>
    /// Lengths keep the unit the theme wrote them in; Figma, which has only pixels, converts
    /// <c>rem</c> at 16px. The output is stable for stable input, so it can be checked in.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// File.WriteAllText("light.tokens.json", DesignTokens.write Tokens.defaults.Light Tokens.defaults.SyntaxLight)
    /// </code>
    /// </example>
    /// <param name="tokens">The <c>--nacara-*</c> properties.</param>
    /// <param name="syntax">The <c>--tok-*</c> highlighting colours.</param>
    let write (tokens: Tokens) (syntax: SyntaxTokens) =
        let document = JsonObject()

        for prefix, root in [ section "nacara" tokens; section "tok" syntax ] do
            document[prefix] <- root

        // Relaxed escaping leaves the quotes in a font stack readable, not \u-escaped, and a
        // fixed newline keeps the document the same on every OS.
        let options =
            JsonSerializerOptions(
                WriteIndented = true,
                NewLine = "\n",
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            )

        document.ToJsonString options + "\n"

    let private number (value: decimal) =
        (Math.Round(value, 4)).ToString("0.####", CultureInfo.InvariantCulture)

    /// <summary>A length in the unit the theme already uses for that token.</summary>
    /// <remarks>A token the theme writes as a bare number, a line height or a weight, stays
    /// one: a design tool that has only pixels calls every number a length.</remarks>
    let private inUnitOf (current: string) (amount: decimal) (unit: string) =
        let existing = length.Match(current.Trim())

        if plainNumber.IsMatch(current.Trim()) && unit = "px" then
            number amount
        else
            match unit, (if existing.Success then existing.Groups[2].Value else unit) with
            | "px", "rem" -> $"%s{number (amount / 16m)}rem"
            | "rem", "px" -> $"%s{number (amount * 16m)}px"
            | _ -> $"%s{number amount}%s{unit}"

    /// <summary>A colour's <c>$value</c> as hex: a hex string, or the object form.</summary>
    let private colorOf (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.String -> Some(value.GetString())
        | JsonValueKind.Object ->
            let alpha =
                match value.TryGetProperty "alpha" with
                | true, alpha when alpha.GetDouble() < 1.0 -> channel (alpha.GetDouble())
                | _ -> ""

            match value.TryGetProperty "hex", value.TryGetProperty "components" with
            | (true, hexValue), _ -> Some $"%s{hexValue.GetString().ToLowerInvariant()}%s{alpha}"
            | _, (true, components) ->
                let rgb =
                    components.EnumerateArray()
                    |> Seq.map (fun part -> channel (part.GetDouble()))
                    |> String.concat ""

                Some $"#%s{rgb}%s{alpha}"
            | _ -> None
        | _ -> None

    /// <summary>A shadow length as CSS: a dimension, or a bare number of pixels. Zero is <c>0</c>.</summary>
    let private shadowLength (value: JsonElement) =
        let amount, unit =
            match value.ValueKind, value.ValueKind = JsonValueKind.Object && fst (value.TryGetProperty "unit") with
            | JsonValueKind.Object, true -> value.GetProperty("value").GetDecimal(), value.GetProperty("unit").GetString()
            | JsonValueKind.Object, false -> value.GetProperty("value").GetDecimal(), "px"
            | _ -> value.GetDecimal(), "px"

        if amount = 0m then "0" else $"%s{number amount}%s{unit}"

    /// <summary>A shadow's layers as a <c>box-shadow</c> value. Spread is left out where it is zero.</summary>
    let private shadowCss (layers: JsonElement seq) =
        let layer (entry: JsonElement) =
            let part name =
                match entry.TryGetProperty(name: string) with
                | true, value -> shadowLength value
                | _ -> "0"

            let inset =
                match entry.TryGetProperty "inset" with
                | true, value when value.ValueKind = JsonValueKind.True -> [ "inset" ]
                | _ -> []

            let spread = part "spread"

            let color =
                match entry.TryGetProperty "color" with
                | true, color -> colorOf color
                | _ -> None

            match color with
            | Some color ->
                inset @ [ part "offsetX"; part "offsetY"; part "blur" ] @ (if spread = "0" then [] else [ spread ]) @ [ color ]
                |> String.concat " "
                |> Some
            | None -> None

        let written = layers |> Seq.map layer |> List.ofSeq

        if not written.IsEmpty && written |> List.forall Option.isSome then
            written |> List.choose id |> String.concat ", " |> Some
        else
            None

    /// <summary>A token's <c>$value</c> as CSS, or nothing for a shape it does not know.</summary>
    let private css (current: string) (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.String ->
            let text = value.GetString()

            if text.StartsWith "{" && text.EndsWith "}" then
                let path = text.Trim('{', '}').Split '.'
                Some $"var(--%s{path[0]}-%s{Array.last path})"
            else
                Some text
        | JsonValueKind.Number -> Some(inUnitOf current (value.GetDecimal()) "px")
        | JsonValueKind.Array when value.EnumerateArray() |> Seq.forall (fun entry -> entry.ValueKind = JsonValueKind.Object) ->
            shadowCss (value.EnumerateArray())
        | JsonValueKind.Array ->
            value.EnumerateArray()
            |> Seq.map (fun family ->
                let name = family.GetString()
                if name.Contains ' ' then $"\"%s{name}\"" else name
            )
            |> String.concat ", "
            |> Some
        | JsonValueKind.Object ->
            match value.TryGetProperty "value", value.TryGetProperty "offsetX" with
            | (true, amount), _ ->
                let unit =
                    match value.TryGetProperty "unit" with
                    | true, unit -> unit.GetString()
                    | _ -> "px"

                Some(inUnitOf current (amount.GetDecimal()) unit)
            | _, (true, _) -> shadowCss [ value ]
            | _ -> colorOf value
        | _ -> None

    /// <summary>Every token under one prefix, by the last segment of its path.</summary>
    let private tokensUnder (prefix: string) (json: string) =
        use document = JsonDocument.Parse json

        let rec walk (element: JsonElement) (name: string) =
            seq {
                match element.TryGetProperty "$value" with
                | true, value -> yield name, value.Clone()
                | _ ->
                    for property in element.EnumerateObject() do
                        if property.Value.ValueKind = JsonValueKind.Object && not (property.Name.StartsWith "$") then
                            yield! walk property.Value property.Name
            }

        match document.RootElement.TryGetProperty prefix with
        | true, root -> walk root prefix |> Map.ofSeq
        | _ -> Map.empty

    let private read<'Record> (prefix: string) (json: string) (record: 'Record) =
        let found = tokensUnder prefix json

        let values =
            FSharpType.GetRecordFields typeof<'Record>
            |> Array.map (fun field ->
                let current = string (field.GetValue record)

                found
                |> Map.tryFind (Tokens.kebab field.Name)
                |> Option.bind (css current)
                |> Option.defaultValue current
                |> box
            )

        FSharpValue.MakeRecord(typeof<'Record>, values) :?> 'Record

    /// <summary>The <c>--nacara-*</c> properties a design tokens document sets, over the ones given.</summary>
    /// <remarks>
    /// <para>
    /// A token the document leaves out keeps its value, and a token the record has no field
    /// for is ignored. Colours may be hex strings or the object form, with <c>hex</c> or
    /// <c>components</c>; lengths may be <c>{ value, unit }</c> or a bare number of pixels, and
    /// come back in the unit the record already used, so a Figma round trip leaves
    /// <c>0.25rem</c> as <c>0.25rem</c> rather than <c>4px</c>.
    /// </para>
    /// <para>Shaped to pass straight to <see cref="M:Partas.Nacara.Theme.Theme.lightTokens" />
    /// or <see cref="M:Partas.Nacara.Theme.Theme.darkTokens" />.</para>
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults
    /// |> Theme.lightTokens (DesignTokens.readTokens (File.ReadAllText "design/tokens/light.tokens.json"))
    /// </code>
    /// </example>
    /// <param name="json">The document.</param>
    /// <param name="tokens">The tokens it applies to.</param>
    let readTokens (json: string) (tokens: Tokens) = read "nacara" json tokens

    /// <summary>The <c>--tok-*</c> highlighting colours a design tokens document sets, over the ones given.</summary>
    /// <remarks>Read the way <see cref="M:Partas.Nacara.Theme.DesignTokens.readTokens" /> reads,
    /// and shaped for <see cref="M:Partas.Nacara.Theme.Theme.lightSyntax" /> or
    /// <see cref="M:Partas.Nacara.Theme.Theme.darkSyntax" />.</remarks>
    /// <param name="json">The document.</param>
    /// <param name="syntax">The colours it applies to.</param>
    let readSyntax (json: string) (syntax: SyntaxTokens) = read "tok" json syntax

    /// <summary>Both colour schemes' tokens and highlighting colours, read from a light and a dark document.</summary>
    /// <remarks>
    /// Each document is read over the scheme it replaces, the way
    /// <see cref="M:Partas.Nacara.Theme.DesignTokens.readTokens" /> and
    /// <see cref="M:Partas.Nacara.Theme.DesignTokens.readSyntax" /> read, so a token either
    /// leaves out keeps its value. Shaped for <see cref="M:Partas.Nacara.Theme.Theme.tokens" />.
    /// </remarks>
    /// <param name="light">The light scheme's document.</param>
    /// <param name="dark">The dark scheme's document.</param>
    /// <param name="tokens">The tokens they apply to.</param>
    let readTheme (light: string) (dark: string) (tokens: ThemeTokens) =
        {
            Light = readTokens light tokens.Light
            Dark = readTokens dark tokens.Dark
            SyntaxLight = readSyntax light tokens.SyntaxLight
            SyntaxDark = readSyntax dark tokens.SyntaxDark
        }

    /// <summary>
    /// The documents in a directory, <c>light.tokens.json</c> and <c>dark.tokens.json</c>, read
    /// as <see cref="M:Partas.Nacara.Theme.DesignTokens.readTheme" /> reads them.
    /// </summary>
    /// <remarks>
    /// These are the names the Figma plugin saves under. A scheme whose document is missing keeps
    /// its tokens. A relative directory resolves against the working directory, which is where
    /// the site is built from.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")
    /// </code>
    /// </example>
    /// <param name="directory">The directory holding the documents.</param>
    /// <param name="tokens">The tokens they apply to.</param>
    let fromDirectory (directory: string) (tokens: ThemeTokens) =
        let document scheme =
            let path = IO.Path.Combine(directory, $"%s{scheme}.tokens.json")
            if IO.File.Exists path then IO.File.ReadAllText path else "{}"

        readTheme (document "light") (document "dark") tokens
