// This file is a derivative work of Nacara.Theme.Default, from the Nacara project:
//   Copyright Maxime Mangel - https://github.com/MangelMaxime/Nacara
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the
// root of this repository. This file was modified from its original form; the
// nature of the changes is described in NOTICE.

namespace Partas.Nacara.Theme

open System
open System.Text
open Microsoft.FSharp.Reflection

/// <summary>The custom properties every other layer reads, named <c>--nacara-*</c>.</summary>
/// <remarks>
/// A field here is a CSS custom property there: the name is the field's, in kebab case, under
/// the <c>--nacara-</c> prefix, so <c>BgSubtle</c> is <c>--nacara-bg-subtle</c> and
/// <c>Space12</c> is <c>--nacara-space-12</c>. Values are CSS, unparsed - a length, a colour,
/// a <c>var()</c> reading another token, whatever the property accepts.
/// </remarks>
type Tokens =
    {
        FontSans: string
        FontMono: string
        Space1: string
        Space2: string
        Space3: string
        Space4: string
        Space6: string
        Space8: string
        Space12: string
        Radius: string
        RadiusSm: string
        ContentWidth: string
        SplashWidth: string
        LayoutGap: string
        SidebarWidth: string
        TocWidth: string
        NavbarHeight: string
        NavbarOpacity: string
        NavbarBlur: string
        ControlHeight: string
        ControlRadius: string
        Bg: string
        BgSubtle: string
        BgRaised: string
        Border: string
        Text: string
        TextMuted: string
        Primary: string
        PrimaryContrast: string
        PrimarySubtle: string
        Shadow: string
        /// What floats over the page. Reads <c>Shadow</c> unless you give it its own value.
        ShadowFloating: string
        Note: string
        Tip: string
        Warning: string
        Danger: string
        Heading: string
        CodeInlineBg: string
        CodeInlineBorder: string
        CodeInlineText: string
        /// How tall a code block grows before it scrolls. <c>none</c> lets it run.
        CodeMaxHeight: string
    }

/// <summary>The syntax highlighting colours, named <c>--tok-*</c>.</summary>
/// <remarks>Same naming rule as <see cref="T:Partas.Nacara.Theme.Tokens"/>, under the
/// <c>--tok-</c> prefix: <c>Keyword</c> is <c>--tok-keyword</c>.</remarks>
type SyntaxTokens =
    {
        Comment: string
        String: string
        Number: string
        Constant: string
        Constructor: string
        Property: string
        Escape: string
        Keyword: string
        Operator: string
        Function: string
        Type: string
        Namespace: string
        Variable: string
        Parameter: string
        Punctuation: string
        Tag: string
        Attribute: string
        Preprocessor: string
        Invalid: string
        Inserted: string
        Deleted: string
    }

/// <summary>Every custom property the theme defines, in both colour schemes.</summary>
/// <remarks>
/// The dark records are read as overrides: a value identical to its light counterpart is not
/// written twice, so the dark block carries only what actually changes.
/// </remarks>
type ThemeTokens =
    {
        Light: Tokens
        Dark: Tokens
        SyntaxLight: SyntaxTokens
        SyntaxDark: SyntaxTokens
    }

/// <summary>The theme's design tokens, and the stylesheet they render to.</summary>
[<RequireQualifiedAccess>]
module Tokens =

    /// <summary>A record field's name as its CSS custom property spelling.</summary>
    /// <remarks>
    /// Lower case, with a hyphen before each capital and before a run of digits, so
    /// <c>PrimarySubtle</c> is <c>primary-subtle</c> and <c>Space12</c> is <c>space-12</c> -
    /// one run of digits, not one hyphen per digit.
    /// </remarks>
    let private kebab (name: string) =
        let builder = StringBuilder()

        name
        |> Seq.iteri (fun index character ->
            let breaks =
                index > 0
                && (Char.IsUpper character
                    || (Char.IsDigit character && not (Char.IsDigit name[index - 1])))

            if breaks then
                builder.Append '-' |> ignore

            builder.Append(Char.ToLowerInvariant character) |> ignore
        )

        builder.ToString()

    /// <summary>The custom properties a token record declares, in declaration order.</summary>
    /// <remarks>
    /// Read by reflection rather than written out a second time: a field added to the record
    /// is a property in the stylesheet without anywhere else to remember to change, which is
    /// the whole reason the tokens are a record and not a string.
    /// </remarks>
    let private declarations (prefix: string) (record: obj) =
        FSharpType.GetRecordFields(record.GetType())
        |> Array.map (fun field -> $"--%s{prefix}-%s{kebab field.Name}", string (field.GetValue record))
        |> Array.toList

    let private block (selector: string) (lines: string list) =
        let body = lines |> List.map (fun line -> $"    %s{line}") |> String.concat "\n"
        $"%s{selector} {{\n%s{body}\n}}"

    let private declared (properties: (string * string) list) =
        properties |> List.map (fun (name, value) -> $"%s{name}: %s{value};")

    /// <summary>Only the properties whose dark value differs from its light one.</summary>
    let private overrides light dark =
        List.zip light dark
        |> List.choose (fun ((name, lightValue), (_, darkValue)) ->
            if lightValue = darkValue then None else Some(name, darkValue)
        )

    /// <summary>The <c>tokens</c> layer's CSS for a set of tokens.</summary>
    /// <param name="tokens">The tokens to write.</param>
    let render (tokens: ThemeTokens) =
        let light = declarations "nacara" tokens.Light @ declarations "tok" tokens.SyntaxLight
        let dark = declarations "nacara" tokens.Dark @ declarations "tok" tokens.SyntaxDark

        [
            // Registered so it animates and so a bad value is ignored rather than inherited.
            block "@property --nacara-content-width" [ "syntax: \"<length>\";"; "inherits: true;"; "initial-value: 600px;" ]

            block ":root" ("color-scheme: light dark;" :: declared light)

            // The two explicit choices, for a reader who overrode what their system reports.
            block ":root[data-theme=\"light\"]" [ "color-scheme: light;" ]

            block ":root[data-theme=\"dark\"]" ("color-scheme: dark;" :: declared (overrides light dark))
        ]
        |> String.concat "\n\n"

    let private lightTokens =
        {
            FontSans = "ui-sans-serif, system-ui, -apple-system, \"Segoe UI\", Roboto, sans-serif"
            FontMono = "ui-monospace, \"JetBrains Mono\", \"Fira Code\", \"SFMono-Regular\", Menlo, monospace"
            Space1 = "0.25rem"
            Space2 = "0.5rem"
            Space3 = "0.75rem"
            Space4 = "1rem"
            Space6 = "1.5rem"
            Space8 = "2rem"
            Space12 = "3rem"
            Radius = "0.5rem"
            RadiusSm = "0.25rem"
            ContentWidth = "75ch"
            SplashWidth = "68rem"
            LayoutGap = "var(--nacara-space-8)"
            SidebarWidth = "17rem"
            TocWidth = "15rem"
            NavbarHeight = "3.5rem"
            NavbarOpacity = "85%"
            NavbarBlur = "8px"
            ControlHeight = "2.25rem"
            ControlRadius = "var(--nacara-radius-sm)"
            Bg = "#ffffff"
            BgSubtle = "#f6f7f9"
            BgRaised = "#ffffff"
            Border = "#d0d7de"
            Text = "#1b1f24"
            TextMuted = "#5a6470"
            Primary = "#6669d7"
            PrimaryContrast = "#ffffff"
            PrimarySubtle = "#ececfb"
            Shadow = "0 1px 2px rgb(16 24 40 / 6%), 0 4px 12px rgb(16 24 40 / 6%)"
            ShadowFloating = "var(--nacara-shadow)"
            Note = "#6669d7"
            Tip = "#17935f"
            Warning = "#b5730c"
            Danger = "#cf3b3b"
            Heading = "var(--nacara-text)"
            CodeInlineBg = "var(--nacara-bg-subtle)"
            CodeInlineBorder = "var(--nacara-border)"
            CodeInlineText = "currentColor"
            CodeMaxHeight = "none"
        }

    let private darkTokens =
        { lightTokens with
            Bg = "#0f1419"
            BgSubtle = "#161c23"
            BgRaised = "#1a2129"
            Border = "#2a333d"
            Text = "#e6edf3"
            TextMuted = "#9aa7b4"
            Primary = "#9b9ef0"
            PrimaryContrast = "#0f1419"
            PrimarySubtle = "#21224a"
            Shadow = "0 1px 2px rgb(0 0 0 / 40%), 0 4px 14px rgb(0 0 0 / 35%)"
            Note = "#9b9ef0"
            Tip = "#56d4a0"
            Warning = "#e2b23c"
            Danger = "#ff7b72"
        }

    let private lightSyntax =
        {
            Comment = "#a0a1a7"
            String = "#50a14f"
            Number = "#986801"
            Constant = "#986801"
            Constructor = "#4078f2"
            Property = "#e45649"
            Escape = "#0184bc"
            Keyword = "#a626a4"
            Operator = "#0184bc"
            Function = "#4078f2"
            Type = "#c18401"
            Namespace = "#4078f2"
            Variable = "#383a42"
            Parameter = "#383a42"
            Punctuation = "#383a42"
            Tag = "#e45649"
            Attribute = "#986801"
            Preprocessor = "#a626a4"
            Invalid = "#e45649"
            Inserted = "#50a14f"
            Deleted = "#e45649"
        }

    let private darkSyntax =
        {
            Comment = "#7f848e"
            String = "#98c379"
            Number = "#d19a66"
            Constant = "#d19a66"
            Constructor = "#61afef"
            Property = "#e06c75"
            Escape = "#56b6c2"
            Keyword = "#c678dd"
            Operator = "#56b6c2"
            Function = "#61afef"
            Type = "#e5c07b"
            Namespace = "#61afef"
            Variable = "#abb2bf"
            Parameter = "#abb2bf"
            Punctuation = "#abb2bf"
            Tag = "#e06c75"
            Attribute = "#d19a66"
            Preprocessor = "#c678dd"
            Invalid = "#e06c75"
            Inserted = "#98c379"
            Deleted = "#e06c75"
        }

    /// <summary>The tokens the theme ships with.</summary>
    let defaults =
        {
            Light = lightTokens
            Dark = darkTokens
            SyntaxLight = lightSyntax
            SyntaxDark = darkSyntax
        }
