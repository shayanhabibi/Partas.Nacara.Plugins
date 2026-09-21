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
            else begin
                let keyStart = index

                while (index < arguments.Length
                       && not (Char.IsWhiteSpace arguments[index])
                       && arguments[index] <> '=') do
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
            end

        match failure with
        | Some reason -> Error reason
        | None -> Ok $"""{{{String.Join(", ", pairs)}}}"""
