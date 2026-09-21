namespace Nacara.Plugins

open Feliz.ViewEngine
open Nacara.Core

/// <summary>How much a fault reported by a directive matters.</summary>
/// <remarks>
/// Neither value fails the build, and neither can: a <c>Registry</c>-scoped
/// <c>IMarkdownExtension</c> has no route to stop one. What the severity decides is the
/// label the message is written under, which - alongside the <c>nacara-directive-error</c>
/// element on the page - is the whole of what a directive has to say with.
/// </remarks>
[<RequireQualifiedAccess>]
type internal FaultSeverity =
    /// The directive could not do what the author asked of it.
    | Error
    /// Worth saying, though the directive carried on regardless.
    | Warning

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
            /// Decoded arguments of enclosing directives, nearest first.
            Ancestors: obj list
            /// Position among siblings of the same name under one parent, from zero.
            SiblingIndex: int
            /// How many directives enclose this one.
            Nesting: int
            /// Says something went wrong, in whatever way the build wants it said.
            Report: FaultSeverity * string -> unit
        }

    /// <summary>The nearest enclosing directive whose arguments are the given type.</summary>
    member this.TryAncestor<'T>() =
        this.Ancestors
        |> List.tryPick (fun ancestor ->
            match box ancestor with
            | :? 'T as value -> Some value
            | _ -> None)

    /// <summary>The immediately enclosing directive's arguments, if they are the given type.</summary>
    member this.TryParent<'T>() =
        match this.Ancestors with
        | parent :: _ ->
            match box parent with
            | :? 'T as value -> Some value
            | _ -> None
        | [] -> None

    /// <summary>Position among siblings of the same name under one parent, from zero.</summary>
    member this.Index = this.SiblingIndex

    /// <summary>How many directives enclose this one.</summary>
    member this.Depth = this.Nesting

    /// <summary>Report that the directive could not do what was asked of it.</summary>
    /// <remarks>
    /// This does not fail the build and cannot: nothing wires a diagnostic sink through to
    /// a <c>Registry</c>-scoped <c>IMarkdownExtension</c>, so there is nowhere for a fatal
    /// error to be raised to. What happens is that the message is written to stderr under
    /// an <c>error:</c> label, naming the directive it came from. What the page shows is
    /// whatever the render function went on to return.
    /// </remarks>
    member this.Error(message: string) = this.Report(FaultSeverity.Error, message)

    /// <summary>Report something worth saying that did not stop the directive.</summary>
    /// <remarks>
    /// The same channel as <c>Error</c>, under a <c>warning:</c> label instead.
    /// </remarks>
    member this.Warn(message: string) = this.Report(FaultSeverity.Warning, message)

/// <summary>A directive: its name, how to read its arguments, what to render.</summary>
/// <remarks>
/// Neither function is generic, because directives of different argument types sit in one
/// list. The type the author declared is restored inside them, which is why they are not
/// public.
/// </remarks>
type Directive =
    internal
        {
            /// The word after <c>:::</c>.
            DirectiveName: string
            /// Reads the opening line's arguments, boxed so directives of different argument
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

/// <summary>Declares a directive that an author writes as <c>:::name</c>.</summary>
[<RequireQualifiedAccess>]
module Directive =

    /// <summary>Begin declaring a directive: its name and how to read its arguments.</summary>
    /// <param name="name">The word an author writes after <c>:::</c>.</param>
    /// <param name="decoder">How the directive's opening-line arguments are read.</param>
    let create (name: string) (decoder: Decoder<'T>) : DirectiveBuilder<'T> =
        {
            BuilderName = name
            Decoder = decoder
        }

    /// <summary>Finish declaring a directive by saying how to render it.</summary>
    /// <param name="render">
    /// Given the context, the decoded arguments, and the already-rendered body, returns the
    /// element placed wherever the directive belongs.
    /// </param>
    /// <param name="builder">The directive declared by <see cref="create"/>.</param>
    let render
        (render: DirectiveContext -> 'T -> ReactElement -> ReactElement)
        (builder: DirectiveBuilder<'T>)
        : Directive =
        {
            DirectiveName = builder.BuilderName
            Decode =
                fun line arguments ->
                    Arguments.toFlowMapping arguments
                    |> Result.bind (fun yaml ->
                        Yaml.decodeWithOffset line builder.Decoder yaml
                        |> Result.mapError (fun error ->
                            // The offset exists to turn a position in the flow mapping this
                            // module synthesised into one on the page, and it is only worth
                            // paying for if the author is told. `line` is
                            // `CustomContainer.Line`, which Markdig counts from zero, and
                            // the flow mapping is a single line, so its own position is
                            // line 1: the sum is already the line a reader counts, and
                            // adding one again would point at the line below.
                            //
                            // The column is left out on purpose. It is a column into the
                            // flow mapping, not into what the author typed, so it would
                            // point at a character that is not on their line.
                            let position = $"line %d{error.Line}"

                            match error.Path with
                            | "" -> $"%s{position}: %s{error.Message}"
                            | path -> $"%s{position}: %s{path}: %s{error.Message}"))
                    |> Result.map box
            RenderWith = fun context value body -> render context (value :?> 'T) body
        }

    /// <summary>Read a line's arguments, the directive already declared.</summary>
    /// <param name="directive">The directive whose decoder is used.</param>
    /// <param name="line">Where the directive starts, so positions are reported there.</param>
    /// <param name="arguments">Everything after the directive's name.</param>
    /// <remarks>
    /// Internal: it takes a <c>Directive</c> whose every field is internal and hands back an
    /// <c>obj</c> that only <c>RenderWith</c> knows how to unbox, so there is nothing a
    /// consumer could do with it. The renderer and the tests are the callers.
    /// </remarks>
    let internal decodeArguments (directive: Directive) (line: int) (arguments: string) : Result<obj, string> =
        directive.Decode line arguments
