# Custom `:::` Directives Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin that lets an author define a `:::name` markdown directive from a
decoder and a Feliz render function, with typed access to enclosing directives.

**Architecture:** One `IMarkdownExtension` contributed through `Registry.extra`, which the
markdown plugin already reads. Its HTML renderer accepts only containers naming a
registered directive, so Nacara's three built-ins fall through untouched. Arguments on the
opening line are tokenised into a YAML flow mapping and read with Nacara's own `Decoder`.
The body is captured by swapping the renderer's `Writer`.

**Tech Stack:** F# / net10.0, Markdig 1.3.2, Feliz.ViewEngine 1.0.3, Nacara.Core
2.0.0-beta.6, Expecto 11.0.0-alpha8.

**Spec:** `docs/superpowers/specs/2026-09-22-custom-directives-design.md`

## Global Constraints

- Target framework `net10.0`; `LangVersion` `latest`.
- Namespace for public API is `Nacara.Plugins`, following
  `src/Partas.Nacara.Plugins.Tailwind/Program.fs:1`. `RootNamespace` and `AssemblyName`
  are `Partas.Nacara.Plugins.Directives`.
- `Directory.Build.props` sets `GenerateDocumentationFile=true` repo-wide, so **every
  public type and member needs an XML doc comment** or the build emits warnings.
- CSS class names use the `nacara-` prefix.
- The project may reference `Partas.Nacara.Theme.Contracts` but **never** the theme.
- All shell commands are prefixed with `rtk`, including inside `&&` chains.
- Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Verified API facts

These were confirmed by reflection against the real assemblies. Do not re-derive them.

| Fact | Value |
|---|---|
| `CustomContainer.Info` | `string` — the word after `:::`, e.g. `"note"` |
| `CustomContainer.Arguments` | `string` — the rest, e.g. `title="hi there" level=2` |
| `MarkdownObject.Line` | `int`, settable, 0-based |
| `Block.Parent` | `ContainerBlock` |
| `TextRendererBase.Writer` | `TextWriter`, **settable** — this is the body-capture hook |
| `RendererBase.WriteChildren` | present |
| `RendererBase.ObjectRenderers` | present, ordered; first accepting renderer wins |
| `MarkdownParserContext.Properties` | `Dictionary<obj, obj>` |
| `Nacara.Core.Decoder<'T>` | `string -> YamlNode -> Result<'T, DecodeError>` |
| `Yaml.decodeWithOffset` | `int -> Decoder<'T> -> string -> Result<'T, DecodeError>` |
| Markdig version used by Nacara | `1.3.2` (genuine xoofx package, post-1.0) |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Partas.Nacara.Plugins.Directives/Arguments.fs` | Tokenise the argument string into a YAML flow mapping. Pure, no Markdig. |
| `src/Partas.Nacara.Plugins.Directives/Types.fs` | `Directive`, `DirectiveBuilder<'T>`, `DirectiveContext`, and the `Directive` module that builds them. `Renderer.fs` needs `Directive.decodeArguments`, so it cannot wait for `Directives.fs`. |
| `src/Partas.Nacara.Plugins.Directives/Context.fs` | The ancestor stack and sibling indexing. Pure, no Markdig types in its signature where avoidable. |
| `src/Partas.Nacara.Plugins.Directives/Renderer.fs` | The Markdig `HtmlObjectRenderer` and `IMarkdownExtension`. |
| `src/Partas.Nacara.Plugins.Directives/Directives.fs` | Public API for putting directives into a build: `Directives.create`, `Directives.register`. |
| `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs` | Expecto tests. Added **before** `Main.fs` in the fsproj. |

Compile order in the fsproj is exactly the order above.

---

### Task 1: Project scaffold

**Files:**
- Create: `src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj`
- Create: `src/Partas.Nacara.Plugins.Directives/Arguments.fs`
- Modify: `Partas.Nacara.Plugins.slnx`
- Modify: `tests/Partas.Nacara.Plugins.Tests/Partas.Nacara.Plugins.Tests.fsproj`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable project and a test file wired into the Expecto run.

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Library</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <LangVersion>latest</LangVersion>
        <RootNamespace>Partas.Nacara.Plugins.Directives</RootNamespace>
        <AssemblyName>Partas.Nacara.Plugins.Directives</AssemblyName>
        <PackageId>Partas.Nacara.Plugins.Directives</PackageId>
        <Title>Partas.Nacara.Plugins.Directives</Title>
        <Description>Author custom ::: directives for Nacara from a decoder and a Feliz render function.</Description>
        <PackageReadmeFile>README.md</PackageReadmeFile>
        <Version>0.1.0</Version><AssemblyVersion>0.0.0.0</AssemblyVersion>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="Arguments.fs" />
        <None Include="../../README.md" Pack="true" PackagePath="\" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Nacara.Core" Version="2.0.0-beta.6" />
        <PackageReference Include="Markdig" Version="1.3.2" />
        <PackageReference Include="Feliz.ViewEngine" Version="1.0.3" />
        <ProjectReference Include="..\Partas.Nacara.Theme.Contracts\Partas.Nacara.Theme.Contracts.fsproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a placeholder `Arguments.fs` so the project compiles**

```fsharp
namespace Nacara.Plugins

/// <summary>Reads the arguments written on a directive's opening line.</summary>
module internal Arguments =
    /// <summary>Placeholder, replaced in the next task.</summary>
    let toFlowMapping (_arguments: string) : Result<string, string> = Ok "{}"
```

- [ ] **Step 3: Add the project to the solution**

In `Partas.Nacara.Plugins.slnx`, inside `<Folder Name="/src/">`, after the DaisyUI line:

```xml
    <Project Path="src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj" />
```

- [ ] **Step 4: Wire the test project**

In `tests/Partas.Nacara.Plugins.Tests/Partas.Nacara.Plugins.Tests.fsproj`, change the
first `ItemGroup` to put `DirectivesTests.fs` before `Main.fs` (Expecto's entry point must
compile last):

```xml
    <ItemGroup>
        <Compile Include="Tests.fs"/>
        <Compile Include="DirectivesTests.fs"/>
        <Compile Include="Main.fs"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj" />
    </ItemGroup>
```

- [ ] **Step 5: Write a test proving the wiring works**

Create `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`:

```fsharp
module Partas.Nacara.Plugins.Tests.Directives

open Expecto
open Nacara.Plugins

[<Tests>]
let tests =
    testList "directives" [
        test "the project is referenced and compiles" {
            Expect.equal (Arguments.toFlowMapping "") (Ok "{}") "placeholder answers"
        }
    ]
```

`Arguments` is `internal`, so add to the Directives project an `AssemblyInfo` entry. Create
`src/Partas.Nacara.Plugins.Directives/AssemblyInfo.fs` as the **first** compiled file:

```fsharp
namespace Partas.Nacara.Plugins.Directives

open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo("Partas.Nacara.Plugins.Tests")>]
do ()
```

and add `<Compile Include="AssemblyInfo.fs" />` above `Arguments.fs`.

- [ ] **Step 6: Build and run**

```bash
rtk dotnet build Partas.Nacara.Plugins.slnx && rtk dotnet test tests/Partas.Nacara.Plugins.Tests
```

Expected: build succeeds with 0 warnings, 1 test passes.

- [ ] **Step 7: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives tests/Partas.Nacara.Plugins.Tests Partas.Nacara.Plugins.slnx && rtk git commit -m "chore(directives): scaffold the directives plugin project

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Spike — can a directive know its page?

The spec records two unknowns this settles: whether a diagnostic can name the source
**file**, and whether a plugin can report diagnostics at all from inside markdown
rendering. Time-boxed to one hour. Produces an answer and a paragraph, not shipped code.

**Files:**
- Create: `docs/superpowers/specs/2026-09-22-custom-directives-design.md` (append a
  "Spike result" section)

- [ ] **Step 1: Determine whether Nacara passes a parser context**

Write a throwaway extension that records whether a `MarkdownParserContext` reaches it, and
what is in `Properties`. Register it in `docs/Site.fs`, build the docs, print the findings.

```fsharp
type ProbeExtension() =
    interface Markdig.IMarkdownExtension with
        member _.Setup(pipeline: Markdig.MarkdownPipelineBuilder) =
            pipeline.DocumentProcessed.Add(fun doc ->
                printfn "PROBE doc keys: %A" (doc.GetData("nacara-page")))
        member _.Setup(_pipeline, _renderer) = ()
```

Run: `rtk dotnet run --project docs`
Expected: either a page-shaped value, or `null`. Record which.

- [ ] **Step 2: Determine how a plugin reports a diagnostic mid-render**

Search `Nacara.Core.xml` and the markdown plugin for a `DiagnosticSink` reachable from a
`Registry` or from `pipelineFor`.

```bash
rtk grep -rn "DiagnosticSink" /c/Users/shaya/.nuget/packages/nacara.core/2.0.0-beta.6/lib/net10.0/Nacara.Core.xml
```

- [ ] **Step 3: Record the answer in the spec**

Append a `## Spike result` section to the design doc stating, in two or three sentences,
which of these holds:

- **Best case:** page reachable → `DirectiveContext.Error` produces a real `Diagnostic`
  via `DecodeError.toDiagnostic` and `Diagnostic.at`, with file and line.
- **Degraded case:** page not reachable → `DirectiveContext.Error` renders a visible
  error element (`Html.div [ prop.className "nacara-directive-error" ]`) into the page and
  writes to stderr. An author sees the fault in the page they are writing.

Task 5 implements whichever case this establishes. Everything else is unaffected.

- [ ] **Step 4: Remove the probe and commit the spec update**

```bash
rtk git checkout docs/Site.fs && rtk git add docs/superpowers/specs/2026-09-22-custom-directives-design.md && rtk git commit -m "docs(spec): record whether a directive can reach its page

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Argument tokeniser

Turns `title="Watch out" level=2` into `{title: "Watch out", level: 2}`, which YamlDotNet
parses as a flow mapping. Bare values stay unquoted so YAML types them: `2` is an int,
`true` a bool. A key with no `=` is a flag and becomes `true`.

**Files:**
- Modify: `src/Partas.Nacara.Plugins.Directives/Arguments.fs`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Arguments.toFlowMapping : string -> Result<string, string>`. `Ok` carries YAML
  source; `Error` carries a human-readable reason.

- [ ] **Step 1: Write failing tests**

Replace the `testList` in `DirectivesTests.fs`:

```fsharp
module Partas.Nacara.Plugins.Tests.Directives

open Expecto
open Nacara.Plugins

let private mapping input = Arguments.toFlowMapping input

[<Tests>]
let tests =
    testList "directives" [
        testList "argument tokeniser" [
            test "no arguments is an empty mapping" {
                Expect.equal (mapping "") (Ok "{}") "empty"
            }

            test "whitespace only is an empty mapping" {
                Expect.equal (mapping "   ") (Ok "{}") "blank"
            }

            test "a bare pair" {
                Expect.equal (mapping "level=2") (Ok "{level: 2}") "unquoted value kept raw"
            }

            test "a quoted value may contain spaces" {
                Expect.equal
                    (mapping "title=\"Watch out\"")
                    (Ok "{title: \"Watch out\"}")
                    "quotes preserved"
            }

            test "a quoted value may contain an equals sign" {
                Expect.equal
                    (mapping "expr=\"a = b\"")
                    (Ok "{expr: \"a = b\"}")
                    "the first equals splits, later ones are data"
            }

            test "several pairs" {
                Expect.equal
                    (mapping "title=\"Watch out\" level=2")
                    (Ok "{title: \"Watch out\", level: 2}")
                    "comma separated"
            }

            test "a key with no value is a flag" {
                Expect.equal (mapping "collapsible") (Ok "{collapsible: true}") "bare flag"
            }

            test "a quote that is never closed is an error" {
                let result = mapping "title=\"Watch out"
                Expect.isError result "unterminated quote rejected"
            }

            test "a double quote inside a quoted value is escaped" {
                Expect.equal
                    (mapping "title=\"say \\\"hi\\\"\"")
                    (Ok "{title: \"say \\\"hi\\\"\"}")
                    "escape survives"
            }
        ]
    ]
```

- [ ] **Step 2: Run the tests and watch them fail**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "argument tokeniser"
```

Expected: FAIL — the placeholder returns `{}` for everything.

- [ ] **Step 3: Implement the tokeniser**

Replace `src/Partas.Nacara.Plugins.Directives/Arguments.fs`:

```fsharp
namespace Nacara.Plugins

open System
open System.Text

/// <summary>Reads the arguments written on a directive's opening line.</summary>
/// <remarks>
/// Markdig hands over everything after the directive's name as one string. Rather than
/// invent a syntax for it, it is reshaped into a YAML flow mapping and given to the same
/// decoders that read front matter - so <c>level=2</c> arrives as an int because YAML
/// says so, not because this module guessed.
/// </remarks>
module internal Arguments =

    [<Literal>]
    let private Quote = '"'

    /// <summary>Read a value that is wrapped in quotes, returning it still wrapped.</summary>
    /// <remarks>
    /// The quotes are kept because YAML wants them: it is what stops a value with a space
    /// in it from ending the entry. A backslash escapes the character after it, which is
    /// how a quote gets into a quoted value.
    /// </remarks>
    let private readQuoted (input: string) (start: int) =
        let value = StringBuilder().Append(Quote)
        let mutable index = start + 1
        let mutable closed = false

        while not closed && index < input.Length do
            match input[index] with
            | '\\' when index + 1 < input.Length ->
                value.Append('\\').Append(input[index + 1]) |> ignore
                index <- index + 2
            | c when c = Quote ->
                value.Append(Quote) |> ignore
                closed <- true
                index <- index + 1
            | c ->
                value.Append(c) |> ignore
                index <- index + 1

        if closed then Ok(value.ToString(), index)
        else Error $"a quoted value was opened but never closed: %s{input.Substring start}"

    /// <summary>Read a value that runs until the next space.</summary>
    let private readBare (input: string) (start: int) =
        let mutable index = start
        while index < input.Length && not (Char.IsWhiteSpace input[index]) do
            index <- index + 1
        input.Substring(start, index - start), index

    /// <summary>Turn an opening line's arguments into a YAML flow mapping.</summary>
    /// <param name="arguments">Everything Markdig found after the directive's name.</param>
    /// <returns>YAML source on success, or why it could not be read.</returns>
    let toFlowMapping (arguments: string) : Result<string, string> =
        let pairs = ResizeArray<string>()
        let mutable index = 0
        let mutable failure = None

        while failure.IsNone && index < arguments.Length do
            if Char.IsWhiteSpace arguments[index] then
                index <- index + 1
            else
                let keyStart = index

                while
                    index < arguments.Length
                    && not (Char.IsWhiteSpace arguments[index])
                    && arguments[index] <> '='
                do
                    index <- index + 1

                let key = arguments.Substring(keyStart, index - keyStart)

                if index < arguments.Length && arguments[index] = '=' then
                    index <- index + 1

                    if index < arguments.Length && arguments[index] = Quote then
                        match readQuoted arguments index with
                        | Ok(value, next) ->
                            pairs.Add $"%s{key}: %s{value}"
                            index <- next
                        | Error reason -> failure <- Some reason
                    else
                        let value, next = readBare arguments index
                        pairs.Add $"%s{key}: %s{value}"
                        index <- next
                else
                    // A key on its own is a flag, which is how a reader writes it anyway.
                    pairs.Add $"%s{key}: true"

        match failure with
        | Some reason -> Error reason
        | None -> Ok $"""{{{String.Join(", ", pairs)}}}"""
```

- [ ] **Step 4: Run the tests and watch them pass**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "argument tokeniser"
```

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives/Arguments.fs tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs && rtk git commit -m "feat(directives): read directive arguments as a YAML flow mapping

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The directive type and its builder

A `Directive` must be storable in a list, so it cannot stay generic in `'T`. The decoder
and render function are boxed behind a non-generic surface, and `DirectiveBuilder<'T>`
carries the type between `create` and `render` so the author never annotates anything.

**Files:**
- Create: `src/Partas.Nacara.Plugins.Directives/Types.fs`
- Modify: `src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: `Arguments.toFlowMapping`.
- Produces:
  - `type DirectiveContext` with `TryAncestor<'T> : unit -> 'T option`,
    `TryParent<'T> : unit -> 'T option`, `Index : int`, `Depth : int`,
    `Error : string -> unit`, `Warn : string -> unit`.
  - `type Directive` with `Name : string`, and internal `Decode` / `Render`.
  - `type DirectiveBuilder<'T>`.
  - `Directive.create : string -> Decoder<'T> -> DirectiveBuilder<'T>`
  - `Directive.render : (DirectiveContext -> 'T -> ReactElement -> ReactElement) -> DirectiveBuilder<'T> -> Directive`
  - `Directive.decodeArguments : Directive -> int -> string -> Result<obj, string>`

- [ ] **Step 1: Write failing tests**

Append to the `testList "directives"` in `DirectivesTests.fs`:

```fsharp
        testList "directive builder" [
            test "a decoder's type survives to the render function" {
                let directive =
                    Directive.create "note" (Decode.object (fun get ->
                        {| Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ args _ -> Html.span [ prop.text (string args.Level) ])

                Expect.equal directive.Name "note" "name kept"
            }

            test "arguments decode into the declared type" {
                let directive =
                    Directive.create "note" (Decode.object (fun get ->
                        {| Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                match Directive.decodeArguments directive 0 "level=2" with
                | Ok value -> Expect.equal (value :?> {| Level: int |}).Level 2 "decoded"
                | Error reason -> failtestf "expected a decode, got %s" reason
            }

            test "a missing required argument is an error" {
                let directive =
                    Directive.create "step" (Decode.object (fun get ->
                        {| Title = get.Required.Field "title" Decode.string |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isError (Directive.decodeArguments directive 0 "") "title is required"
            }

            test "a malformed argument line is an error, not an exception" {
                let directive =
                    Directive.create "note" (Decode.succeed ())
                    |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isError
                    (Directive.decodeArguments directive 0 "title=\"never closed")
                    "tokeniser failure surfaces as Error"
            }
        ]
```

Add these opens at the top of the test file:

```fsharp
open Feliz.ViewEngine
open Nacara.Core
```

- [ ] **Step 2: Run the tests and watch them fail**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "directive builder"
```

Expected: FAIL — `Directive` is not defined.

- [ ] **Step 3: Implement the types**

Create `src/Partas.Nacara.Plugins.Directives/Types.fs`:

```fsharp
namespace Nacara.Plugins

open Feliz.ViewEngine
open Nacara.Core

/// <summary>What a directive can find out about where it sits.</summary>
/// <remarks>
/// Markdig's block tree holds the nesting but not a parent's decoded arguments, so those
/// are carried here instead. Lookup is by type, following <c>Registry.extras</c>: the
/// type is the contract, and a directive that wants its parent asks for the parent's
/// argument type rather than matching on a name.
/// </remarks>
type DirectiveContext =
    internal
        {
            /// Decoded arguments of the enclosing directives, nearest first.
            Ancestors: obj list
            /// Position among the siblings of the same name under one parent, from zero.
            SiblingIndex: int
            /// How many directives enclose this one.
            Nesting: int
            /// Says something went wrong, in whatever way this build can say it.
            Report: string -> unit
        }

    /// <summary>The nearest enclosing directive whose arguments are of this type.</summary>
    member this.TryAncestor<'T>() =
        this.Ancestors |> List.tryPick (function :? 'T as value -> Some value | _ -> None)

    /// <summary>The directly enclosing directive, when its arguments are of this type.</summary>
    member this.TryParent<'T>() =
        match this.Ancestors with
        | (:? 'T as value) :: _ -> Some value
        | _ -> None

    /// <summary>Position among the siblings of the same name, counting from zero.</summary>
    member this.Index = this.SiblingIndex

    /// <summary>How many directives enclose this one.</summary>
    member this.Depth = this.Nesting

    /// <summary>Report a fault that should fail the build.</summary>
    member this.Error(message: string) = this.Report message

    /// <summary>Report something worth saying that is not fatal.</summary>
    member this.Warn(message: string) = this.Report message

/// <summary>A directive: a name, how to read its arguments, and what to render.</summary>
/// <remarks>
/// Neither function is generic, because directives are kept in a list. The type the
/// author declared is restored inside them, which is why they are not public.
/// </remarks>
type Directive =
    internal
        {
            /// The word after <c>:::</c>.
            DirectiveName: string
            /// Reads the opening line, boxed so that directives of different argument
            /// types can sit in one list.
            Decode: int -> string -> Result<obj, string>
            /// Renders the directive, unboxing what <c>Decode</c> produced.
            RenderWith: DirectiveContext -> obj -> ReactElement -> ReactElement
        }

    /// <summary>The word after <c>:::</c> that selects this directive.</summary>
    member this.Name = this.DirectiveName

/// <summary>A directive whose arguments are known but whose rendering is not yet said.</summary>
/// <typeparam name="T">What the arguments decode into.</typeparam>
type DirectiveBuilder<'T> =
    internal
        {
            BuilderName: string
            Decoder: Decoder<'T>
        }
```

Add `<Compile Include="Types.fs" />` after `Arguments.fs` in the fsproj.

- [ ] **Step 4: Implement the builder functions**

Append to `src/Partas.Nacara.Plugins.Directives/Types.fs`, below the type definitions.
It belongs here rather than in `Directives.fs` because `Renderer.fs` calls
`Directive.decodeArguments` and compiles before `Directives.fs` does.

```fsharp
/// <summary>Declares a directive an author can write as <c>:::name</c>.</summary>
[<RequireQualifiedAccess>]
module Directive =

    /// <summary>Begin a directive: its name, and how to read its arguments.</summary>
    /// <param name="name">The word that follows <c>:::</c>.</param>
    /// <param name="decoder">Reads the opening line's arguments, exactly as front matter
    /// is read.</param>
    let create (name: string) (decoder: Decoder<'T>) = { BuilderName = name; Decoder = decoder }

    /// <summary>Say what the directive renders, which completes it.</summary>
    /// <param name="render">Given where it sits, its arguments and its already-rendered
    /// body, returns the markup. The body is markdown, so only Markdig can render it - it
    /// arrives done, to be placed wherever it belongs.</param>
    /// <param name="builder">The directive being declared.</param>
    let render
        (render: DirectiveContext -> 'T -> ReactElement -> ReactElement)
        (builder: DirectiveBuilder<'T>)
        =
        {
            DirectiveName = builder.BuilderName
            Decode =
                fun line arguments ->
                    Arguments.toFlowMapping arguments
                    |> Result.bind (fun yaml ->
                        Yaml.decodeWithOffset line builder.Decoder yaml
                        |> Result.mapError (fun error ->
                            match error.Path with
                            | "" -> error.Message
                            | path -> $"%s{path}: %s{error.Message}"))
                    |> Result.map box
            RenderWith = fun context value body -> render context (value :?> 'T) body
        }

    /// <summary>Read an opening line's arguments, for a directive already declared.</summary>
    /// <param name="directive">The directive whose decoder to use.</param>
    /// <param name="line">Where the directive starts, so positions are reported there.</param>
    /// <param name="arguments">Everything after the directive's name.</param>
    let decodeArguments (directive: Directive) (line: int) (arguments: string) =
        directive.Decode line arguments
```

`DecodeError` carries `Path`, `Message`, `Line` and `Column`, all confirmed by
reflection. No new file is added in this step — `Types.fs` is already in the fsproj.

- [ ] **Step 5: Run the tests and watch them pass**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "directive builder"
```

Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives tests/Partas.Nacara.Plugins.Tests && rtk git commit -m "feat(directives): declare a directive from a decoder and a render function

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Nesting context

`Index` is computed from the block tree rather than counted into a mutable, because the
tree already knows: a container's siblings are its parent's children. The ancestor stack
is genuine state, pushed and popped around each render.

**Files:**
- Create: `src/Partas.Nacara.Plugins.Directives/Context.fs`
- Modify: `src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: `DirectiveContext` from Task 4.
- Produces:
  - `DirectiveStack` — a class with `Push : obj -> unit`, `Pop : unit -> unit`,
    `Current : obj list`, `Depth : int`.
  - `Context.siblingIndex : CustomContainer -> int`.

- [ ] **Step 1: Write failing tests**

Append to `DirectivesTests.fs`:

```fsharp
        testList "nesting" [
            test "an ancestor is found by its type" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})

                let context =
                    { Ancestors = stack.Current
                      SiblingIndex = 0
                      Nesting = stack.Depth
                      Report = ignore }

                Expect.equal (context.TryAncestor<{| Start: int |}>()) (Some {| Start = 1 |}) "found through one level"
            }

            test "the parent is only the nearest" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})

                let context =
                    { Ancestors = stack.Current
                      SiblingIndex = 0
                      Nesting = stack.Depth
                      Report = ignore }

                Expect.isNone (context.TryParent<{| Start: int |}>()) "the grandparent is not the parent"
                Expect.isSome (context.TryParent<{| Title: string |}>()) "the parent is"
            }

            test "popping restores what was there before" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})
                stack.Pop()

                Expect.equal stack.Depth 1 "one left"
                Expect.isSome
                    ({ Ancestors = stack.Current; SiblingIndex = 0; Nesting = 1; Report = ignore }
                        .TryParent<{| Start: int |}>())
                    "the outer one is the parent again"
            }

            test "siblings of the same name are numbered in order" {
                let pipeline = Markdig.MarkdownPipelineBuilder().UseCustomContainers().Build()
                let source = ":::steps\n:::step\nfirst\n:::\n:::step\nsecond\n:::\n:::\n"
                let document = Markdig.Markdown.Parse(source, pipeline)

                let steps =
                    document.Descendants<Markdig.Extensions.CustomContainers.CustomContainer>()
                    |> Seq.filter (fun c -> c.Info = "step")
                    |> Seq.toList

                Expect.equal (steps |> List.map Context.siblingIndex) [ 0; 1 ] "numbered from zero"
            }
        ]
```

Add `open Markdig.Syntax` for `Descendants`.

- [ ] **Step 2: Run the tests and watch them fail**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "nesting"
```

Expected: FAIL — `DirectiveStack` is not defined.

- [ ] **Step 3: Implement**

Create `src/Partas.Nacara.Plugins.Directives/Context.fs`:

```fsharp
namespace Nacara.Plugins

open Markdig.Extensions.CustomContainers
open Markdig.Syntax

/// <summary>The directives currently being rendered, innermost last.</summary>
/// <remarks>
/// Rendering is depth-first and one document at a time, so a plain stack is enough: while
/// a child renders, every directive enclosing it has been pushed and not yet popped.
/// </remarks>
type internal DirectiveStack() =
    let mutable entries: obj list = []

    /// <summary>Note that a directive's body is about to be rendered.</summary>
    member _.Push(arguments: obj) = entries <- arguments :: entries

    /// <summary>Note that it has finished.</summary>
    member _.Pop() =
        entries <-
            match entries with
            | _ :: rest -> rest
            | [] -> []

    /// <summary>The enclosing directives' arguments, nearest first.</summary>
    member _.Current = entries

    /// <summary>How many directives are open.</summary>
    member _.Depth = List.length entries

/// <summary>Where a directive sits, read from the block tree.</summary>
module internal Context =

    /// <summary>Position among the siblings sharing this directive's name.</summary>
    /// <remarks>
    /// Taken from the tree rather than counted while rendering, because the tree already
    /// knows and a count would have to be reset correctly on every parent.
    /// </remarks>
    let siblingIndex (container: CustomContainer) =
        match container.Parent with
        | null -> 0
        | parent ->
            parent
            |> Seq.choose (function
                | :? CustomContainer as sibling when sibling.Info = container.Info -> Some sibling
                | _ -> None)
            |> Seq.toList
            |> List.tryFindIndex (fun sibling -> System.Object.ReferenceEquals(sibling, container))
            |> Option.defaultValue 0
```

Add `<Compile Include="Context.fs" />` between `Types.fs` and `Directives.fs`.

- [ ] **Step 4: Run the tests and watch them pass**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "nesting"
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives tests/Partas.Nacara.Plugins.Tests && rtk git commit -m "feat(directives): let a directive read the ones enclosing it

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: The Markdig renderer

**Files:**
- Create: `src/Partas.Nacara.Plugins.Directives/Renderer.fs`
- Modify: `src/Partas.Nacara.Plugins.Directives/Partas.Nacara.Plugins.Directives.fsproj`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: `Directive`, `DirectiveContext`, `DirectiveStack`, `Context.siblingIndex`.
- Produces:
  - `DirectiveExtension(directives: Directive list)` implementing `IMarkdownExtension`.
  - `Renderer.toHtml : Directive list -> string -> string` — parses and renders a markdown
    string with the directives applied. Used by tests and nothing else.

- [ ] **Step 1: Write failing tests**

Append to `DirectivesTests.fs`:

```fsharp
        testList "rendering" [
            let note =
                Directive.create "note" (Decode.object (fun get ->
                    {| Title = get.Optional.Field "title" Decode.string |}))
                |> Directive.render (fun _ args body ->
                    Html.aside [
                        prop.className "nacara-note"
                        prop.children [
                            match args.Title with
                            | Some title -> Html.p [ prop.className "nacara-note__title"; prop.text title ]
                            | None -> Html.none
                            body
                        ]
                    ])

            test "a registered directive renders through its function" {
                let html = Renderer.toHtml [ note ] ":::note title=\"Careful\"\nbody text\n:::\n"
                Expect.stringContains html "nacara-note" "the class is there"
                Expect.stringContains html "Careful" "the argument was read"
            }

            test "the body is rendered as markdown, not as text" {
                let html = Renderer.toHtml [ note ] ":::note\nsome *emphasis*\n:::\n"
                Expect.stringContains html "<em>emphasis</em>" "markdown in the body still works"
            }

            test "an unregistered directive is left alone" {
                let html = Renderer.toHtml [ note ] ":::unknown\nbody\n:::\n"
                Expect.isFalse (html.Contains "nacara-note") "our renderer did not claim it"
            }

            test "directives nest" {
                let steps =
                    Directive.create "steps" (Decode.succeed {| Start = 1 |})
                    |> Directive.render (fun _ _ body -> Html.ol [ prop.children [ body ] ])

                let step =
                    Directive.create "step" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        Html.li [
                            prop.custom ("data-step", string ctx.Index)
                            prop.children [ body ]
                        ])

                let html =
                    Renderer.toHtml
                        [ steps; step ]
                        ":::steps\n:::step\nfirst\n:::\n:::step\nsecond\n:::\n:::\n"

                Expect.stringContains html "data-step=\"0\"" "first is zero"
                Expect.stringContains html "data-step=\"1\"" "second is one"
            }

            test "a child can read its parent's arguments" {
                let steps =
                    Directive.create "steps" (Decode.object (fun get ->
                        {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ _ body -> Html.ol [ prop.children [ body ] ])

                let step =
                    Directive.create "step" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        match ctx.TryAncestor<{| Start: int |}>() with
                        | Some parent ->
                            Html.li [
                                prop.custom ("data-number", string (parent.Start + ctx.Index))
                                prop.children [ body ]
                            ]
                        | None -> Html.li [ prop.text "orphan" ])

                let html = Renderer.toHtml [ steps; step ] ":::steps start=5\n:::step\nfirst\n:::\n:::\n"
                Expect.stringContains html "data-number=\"5\"" "the parent's start was read"
            }

            test "arguments that do not decode still render the body" {
                let step =
                    Directive.create "step" (Decode.object (fun get ->
                        {| Title = get.Required.Field "title" Decode.string |}))
                    |> Directive.render (fun _ args _ -> Html.h3 args.Title)

                let html = Renderer.toHtml [ step ] ":::step\nthe body\n:::\n"
                Expect.stringContains html "the body" "content is not lost when arguments fail"
            }
        ]
```

- [ ] **Step 2: Run the tests and watch them fail**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "rendering"
```

Expected: FAIL — `Renderer` is not defined.

- [ ] **Step 3: Implement the renderer**

Create `src/Partas.Nacara.Plugins.Directives/Renderer.fs`:

```fsharp
namespace Nacara.Plugins

open System.IO
open Feliz.ViewEngine
open Markdig
open Markdig.Extensions.CustomContainers
open Markdig.Renderers
open Markdig.Renderers.Html

/// <summary>Renders the containers that name a declared directive.</summary>
/// <remarks>
/// Only those: a container naming anything else is refused, so Nacara's own
/// <c>:::steps</c>, <c>:::filetree</c> and <c>:::preview</c> keep falling through to the
/// renderer that has always handled them.
/// </remarks>
type internal DirectiveRenderer(directives: Directive list, stack: DirectiveStack) =
    inherit HtmlObjectRenderer<CustomContainer>()

    let byName =
        directives |> List.map (fun directive -> directive.Name, directive) |> dict

    /// <summary>Render a container's children and hand back what was written.</summary>
    /// <remarks>
    /// The writer is swapped rather than a second renderer built, so every extension and
    /// all of the renderer's state carry into the body - which is what keeps nested
    /// directives, code blocks and highlighting working inside a <c>:::</c>.
    /// </remarks>
    let capture (renderer: HtmlRenderer) (container: CustomContainer) =
        let original = renderer.Writer
        use captured = new StringWriter()
        renderer.Writer <- captured
        renderer.WriteChildren container |> ignore
        renderer.Writer <- original
        captured.ToString()

    override _.TryWrite(renderer: HtmlRenderer, container: CustomContainer) =
        match byName.TryGetValue(container.Info) with
        | false, _ -> false
        | true, directive ->
            let faults = ResizeArray<string>()

            match Directive.decodeArguments directive container.Line container.Arguments with
            | Error reason ->
                // The arguments are unreadable, so the directive cannot be rendered - but
                // the body is the author's writing and is worth more than the wrapper.
                faults.Add reason
                let body = capture renderer container

                Html.div [
                    prop.className "nacara-directive-error"
                    prop.children [
                        Html.p [ prop.text $":::%s{container.Info} - %s{reason}" ]
                        Html.span [ prop.dangerouslySetInnerHTML body ]
                    ]
                ]
                |> Render.htmlView
                |> renderer.Write
                |> ignore

                true
            | Ok arguments ->
                let context =
                    {
                        Ancestors = stack.Current
                        SiblingIndex = Context.siblingIndex container
                        Nesting = stack.Depth
                        Report = faults.Add
                    }

                stack.Push arguments
                let body = capture renderer container
                stack.Pop()

                let element =
                    directive.RenderWith context arguments (Html.span [ prop.dangerouslySetInnerHTML body ])

                renderer.Write(Render.htmlView element) |> ignore

                for fault in faults do
                    eprintfn $"directives: :::%s{container.Info} - %s{fault}"

                true

/// <summary>Teaches a Markdig pipeline about the declared directives.</summary>
type internal DirectiveExtension(directives: Directive list) =
    interface IMarkdownExtension with
        member _.Setup(pipeline: MarkdownPipelineBuilder) =
            // :::name is the custom container syntax, so it has to be on for any of this
            // to parse. Nacara turns it on already; saying so again is harmless.
            pipeline.UseCustomContainers() |> ignore

        member _.Setup(_pipeline: MarkdownPipeline, renderer: IMarkdownRenderer) =
            match renderer with
            | :? HtmlRenderer as html ->
                // First, so that a declared directive is claimed before the renderer
                // Nacara installed sees it. Anything not declared is refused and falls
                // through to that one untouched.
                html.ObjectRenderers.Insert(0, DirectiveRenderer(directives, DirectiveStack()))
            | _ -> ()

/// <summary>Rendering markdown with directives applied, for tests.</summary>
module internal Renderer =

    /// <summary>Render a markdown string with the given directives in force.</summary>
    let toHtml (directives: Directive list) (markdown: string) =
        let pipeline =
            MarkdownPipelineBuilder()
                .UseCustomContainers()
                .Use(DirectiveExtension directives)
                .Build()

        Markdown.ToHtml(markdown, pipeline)
```

Add `<Compile Include="Renderer.fs" />` between `Context.fs` and `Directives.fs`, giving
the final compile order: `AssemblyInfo.fs`, `Arguments.fs`, `Types.fs`, `Context.fs`,
`Renderer.fs`, `Directives.fs`.

- [ ] **Step 4: Run the tests and watch them pass**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "rendering"
```

Expected: PASS, 6 tests. If the nesting test fails because the stack is per-extension
rather than per-document, move `DirectiveStack()` construction into `TryWrite`'s renderer
instance — one stack per `HtmlRenderer`, not one per extension.

- [ ] **Step 5: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives tests/Partas.Nacara.Plugins.Tests && rtk git commit -m "feat(directives): render declared directives through Markdig

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Registration

**Files:**
- Modify: `src/Partas.Nacara.Plugins.Directives/Directives.fs`
- Test: `tests/Partas.Nacara.Plugins.Tests/DirectivesTests.fs`

**Interfaces:**
- Consumes: `DirectiveExtension`, `Directive`.
- Produces:
  - `Directives.create : Directive list -> IPlugin`
  - `Directives.register : Directive list -> Site -> Site`
  - `Directives.duplicates : Directive list -> string list`

- [ ] **Step 1: Write a failing test**

```fsharp
        testList "registration" [
            test "two directives of one name are refused" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.equal (Directives.duplicates [ one; two ]) [ "note" ] "the clash is named"
            }

            test "distinct names are accepted" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "tip" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isEmpty (Directives.duplicates [ one; two ]) "no clash"
            }

            test "creating a plugin with a duplicate name throws at once" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.throws (fun () -> Directives.create [ one; two ] |> ignore) "fails before any page renders"
            }
        ]
```

- [ ] **Step 2: Run and watch it fail**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests --filter "registration"
```

Expected: FAIL — `Directives` is not defined.

- [ ] **Step 3: Implement**

Create `src/Partas.Nacara.Plugins.Directives/Directives.fs`. It did not exist before this
task — Task 4's builder functions went into `Types.fs`, because `Renderer.fs` needs them.

```fsharp
namespace Nacara.Plugins

open Markdig
open Nacara.Core

/// <summary>Puts declared directives into a build.</summary>
[<RequireQualifiedAccess>]
module Directives =

    /// <summary>The names claimed by more than one directive.</summary>
    let duplicates (directives: Directive list) =
        directives
        |> List.countBy _.Name
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map fst

    type private DirectivesPlugin(directives: Directive list) =
        interface IPlugin with
            member _.Name = "directives"

            member _.Configure registry =
                // The markdown plugin reads every IMarkdownExtension a plugin contributed,
                // which is the whole of how this joins the pipeline.
                registry |> Registry.extra (DirectiveExtension(directives) :> IMarkdownExtension)

    /// <summary>A plugin that adds these directives to the build.</summary>
    /// <param name="directives">What an author may write as <c>:::name</c>.</param>
    /// <exception cref="System.Exception">Two directives claim one name.</exception>
    let create (directives: Directive list) =
        match duplicates directives with
        | [] -> DirectivesPlugin(directives) :> IPlugin
        | clashing ->
            // Said now rather than at render time, when it would be one page's problem
            // and whichever directive happened to be first would silently win.
            failwith
                $"""More than one directive is called %s{String.concat ", " clashing}. \
                   A name selects exactly one directive, so the build cannot choose."""

    /// <summary>Add these directives to a site.</summary>
    let register (directives: Directive list) (site: Site) = Site.plugin (create directives) site
```

Add `<Compile Include="Directives.fs" />` as the final entry in the fsproj, after
`Renderer.fs`.

- [ ] **Step 4: Run and watch it pass**

```bash
rtk dotnet test tests/Partas.Nacara.Plugins.Tests
```

Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
rtk git add src/Partas.Nacara.Plugins.Directives tests/Partas.Nacara.Plugins.Tests && rtk git commit -m "feat(directives): register directives as a Nacara plugin

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Prove it end to end in the docs site

Unit tests render through a pipeline this project built. This renders through the one
Nacara builds, which is the only thing that shows the extension is actually read, that it
sits ahead of Nacara's renderer, and that an offered CSS layer reaches the stylesheet.

Earlier in this work two verifications were worthless because they exercised the upstream
`Nacara.Theme.Default` package instead of the local project. Assert against real output
here, and check the assertion can fail.

**Files:**
- Modify: `docs/docs.fsproj`
- Modify: `docs/Site.fs`
- Create: `docs/content/guide/directives.md`

- [ ] **Step 1: Reference the plugin from the docs site**

In `docs/docs.fsproj`, beside the existing theme `ProjectReference`:

```xml
    <ProjectReference Include="..\src\Partas.Nacara.Plugins.Directives\Partas.Nacara.Plugins.Directives.fsproj" />
```

- [ ] **Step 2: Declare a `steps`/`step` pair and register them**

In `docs/Site.fs`, above the site definition:

```fsharp
let private steps =
    Directive.create "steps" (Decode.object (fun get ->
        {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
    |> Directive.render (fun _ _ body -> Html.ol [ prop.className "nacara-steps"; prop.children [ body ] ])

let private step =
    Directive.create "step" (Decode.object (fun get ->
        {| Title = get.Required.Field "title" Decode.string |}))
    |> Directive.render (fun ctx args body ->
        match ctx.TryAncestor<{| Start: int |}>() with
        | None ->
            ctx.Error ":::step only means something inside :::steps"
            Html.none
        | Some parent ->
            Html.li [
                prop.className "nacara-step"
                prop.custom ("data-number", string (parent.Start + ctx.Index))
                prop.children [ Html.h3 args.Title; body ]
            ])
```

Register it alongside the other plugins, and offer the CSS as a layer:

```fsharp
PluginLayers.offer
    { Name = "directives"
      Css = ".nacara-steps { list-style: none; } .nacara-step { margin-block: 1rem; }" }
```

- [ ] **Step 3: Write a page that uses them**

Create `docs/content/guide/directives.md`, matching the front matter of
`docs/content/guide/introduction.md`:

```markdown
---
title: Directives
---

:::steps start=1
:::step title="Install"
Add the package.
:::
:::step title="Register"
Add the plugin to your site.
:::
:::
```

- [ ] **Step 4: Build the docs and assert against the output**

```bash
rtk dotnet run --project docs && rtk grep -c "data-number" docs/output/guide/directives/index.html
```

Expected: the file contains `data-number="1"` and `data-number="2"`, and `nacara-step`.

```bash
rtk grep -o 'data-number="[0-9]*"' docs/output/guide/directives/index.html
```

- [ ] **Step 5: Prove the assertion can fail**

Change `start=1` to `start=7` in the markdown, rebuild, and confirm the numbers become 7
and 8. Change it back. An assertion that cannot fail has not verified anything.

```bash
rtk grep -o 'data-number="[0-9]*"' docs/output/guide/directives/index.html
```

- [ ] **Step 6: Confirm the CSS layer arrived**

```bash
rtk grep -o "@layer nacara.directives" docs/output/assets/css/nacara.*.css
```

Expected: one match, and `nacara.directives` present in the `@layer` order line that
Tailwind writes.

- [ ] **Step 7: Confirm the built-ins still work**

```bash
rtk grep -rn "tip" docs/output/guide/introduction/index.html | head
```

Expected: the existing `:::tip` in `docs/content/guide/introduction.md:22` renders as it
did before. Compare against `git stash` output if unsure.

- [ ] **Step 8: Commit**

```bash
rtk git add docs && rtk git commit -m "test(directives): prove custom directives end to end in the docs site

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Notes for the implementer

- **The `README.md` is packaged** by every project here (`<PackageReadmeFile>`), and the
  repo README lists the plugins. Add a Directives entry to it as part of Task 7.
- **`GenerateDocumentationFile` is on repo-wide.** A missing XML doc on a public member is
  a warning, and this repo currently builds with zero. Keep it that way.
- **A doc comment that names one parameter must name them all** — using
  `<paramref name="..."/>` opts the member into documenting every parameter. Write the
  summary without naming parameters if you do not want to document each one.
- **Do not add `PluginLayers` CSS to the Directives project itself.** v1 ships no CSS. The
  layer offered in Task 8 belongs to the docs site, which is a consumer.
