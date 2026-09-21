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
            /// Decoded arguments of enclosing directives, nearest first.
            Ancestors: obj list
            /// Position among siblings of the same name under one parent, from zero.
            SiblingIndex: int
            /// How many directives enclose this one.
            Nesting: int
            /// Says something went wrong, in whatever way the build wants it said.
            Report: string -> unit
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

    /// <summary>Report a fault that should fail the build.</summary>
    member this.Error(message: string) = this.Report message

    /// <summary>Report something worth saying that is not fatal.</summary>
    member this.Warn(message: string) = this.Report message

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
                            match error.Path with
                            | "" -> error.Message
                            | path -> $"%s{path}: %s{error.Message}"))
                    |> Result.map box
            RenderWith = fun context value body -> render context (value :?> 'T) body
        }

    /// <summary>Read a line's arguments, the directive already declared.</summary>
    /// <param name="directive">The directive whose decoder is used.</param>
    /// <param name="line">Where the directive starts, so positions are reported there.</param>
    /// <param name="arguments">Everything after the directive's name.</param>
    let decodeArguments (directive: Directive) (line: int) (arguments: string) : Result<obj, string> =
        directive.Decode line arguments
