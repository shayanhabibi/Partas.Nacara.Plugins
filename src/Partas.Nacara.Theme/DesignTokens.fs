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
/// colour), <c>size</c> (a length in <c>px</c> or <c>rem</c>), <c>font</c> (a font stack) and
/// <c>css</c> (anything else, kept as written). A token whose value is <c>var()</c> of another
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
    type private Kind =
        | Color
        | Size of value: decimal * unit: string
        | Font
        | Css

    let private hex = Regex(@"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")

    let private length = Regex(@"^(-?\d*\.?\d+)(px|rem)$")

    let private reference = Regex(@"^var\(\s*--([a-z]+)-([a-z0-9-]+)\s*\)$")

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
            Size(Decimal.Parse(measured.Groups[1].Value, CultureInfo.InvariantCulture), measured.Groups[2].Value)
        elif name.StartsWith "font-" then
            Font
        else
            Css

    let private group =
        function
        | Color -> "color"
        | Size _ -> "size"
        | Font -> "font"
        | Css -> "css"

    let private tokenType =
        function
        | Color -> "color"
        | Size _ -> "dimension"
        | Font -> "fontFamily"
        | Css -> "string"

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
                    | Size(amount, unit) ->
                        let dimension = JsonObject()
                        dimension["value"] <- JsonValue.Create amount
                        dimension["unit"] <- JsonValue.Create unit
                        dimension
                    | _ -> JsonValue.Create value

            let token = JsonObject()
            token["$type"] <- JsonValue.Create(tokenType kind)
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
    let private inUnitOf (current: string) (amount: decimal) (unit: string) =
        let existing = length.Match(current.Trim())

        match unit, (if existing.Success then existing.Groups[2].Value else unit) with
        | "px", "rem" -> $"%s{number (amount / 16m)}rem"
        | "rem", "px" -> $"%s{number (amount * 16m)}px"
        | _ -> $"%s{number amount}%s{unit}"

    let private channel (value: float) =
        (int (Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0))).ToString("x2")

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
        | JsonValueKind.Array ->
            value.EnumerateArray()
            |> Seq.map (fun family ->
                let name = family.GetString()
                if name.Contains ' ' then $"\"%s{name}\"" else name
            )
            |> String.concat ", "
            |> Some
        | JsonValueKind.Object ->
            let alpha =
                match value.TryGetProperty "alpha" with
                | true, alpha when alpha.GetDouble() < 1.0 -> channel (alpha.GetDouble())
                | _ -> ""

            match value.TryGetProperty "value", value.TryGetProperty "hex", value.TryGetProperty "components" with
            | (true, amount), _, _ ->
                let unit =
                    match value.TryGetProperty "unit" with
                    | true, unit -> unit.GetString()
                    | _ -> "px"

                Some(inUnitOf current (amount.GetDecimal()) unit)
            | _, (true, hexValue), _ -> Some $"%s{hexValue.GetString().ToLowerInvariant()}%s{alpha}"
            | _, _, (true, components) ->
                let rgb =
                    components.EnumerateArray()
                    |> Seq.map (fun part -> channel (part.GetDouble()))
                    |> String.concat ""

                Some $"#%s{rgb}%s{alpha}"
            | _ -> None
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
    /// |> Theme.lightTokens (DesignTokens.readTokens (File.ReadAllText "design/light.tokens.json"))
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
