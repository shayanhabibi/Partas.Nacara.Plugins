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

    /// <summary>The characters a bare value may not contain.</summary>
    /// <remarks>
    /// A bare value is concatenated into the flow mapping unquoted, which is the whole
    /// reason <c>level=2</c> arrives as an int. That also leaves YAML's own punctuation
    /// free to end the entry early: <c>title=a,b=2</c> would otherwise become two keys
    /// with nothing said about it. Quoting bare values would close the hole and cost the
    /// typing, so a value carrying one of these is refused instead.
    /// </remarks>
    let private flowPunctuation = [| ','; '{'; '}'; '['; ']'; ':' |]

    /// <summary>Turn an opening line's arguments into a YAML flow mapping.</summary>
    /// <param name="arguments">Everything Markdig found after the directive's name.</param>
    /// <returns>YAML source on success, or why it could not be read.</returns>
    /// <remarks>
    /// Everything refused here is refused so that the author hears about their directive
    /// line rather than about a YAML document they never wrote. A mapping that leaves this
    /// function is one whose keys are named, distinct, and paired with a value that cannot
    /// have escaped its own entry.
    /// </remarks>
    let toFlowMapping (arguments: string) : Result<string, string> =
        let pairs = ResizeArray<string>()
        let named = System.Collections.Generic.HashSet<string>()
        let mutable index = 0
        let mutable failure = None

        // Both checks are about the author's line, not about YAML: an unnamed key would
        // reach the decoder as `{: 5}` and a repeated one as a mapping YAML is entitled to
        // read either way round, and in both cases the message would be about a document
        // the author never saw.
        let add (key: string) (entry: string) =
            if key = "" then
                failure <- Some "an argument has no name before its '='"
            else
                match key.IndexOfAny flowPunctuation with
                | -1 ->
                    if not (named.Add key) then
                        failure <- Some $"the argument '%s{key}' is given more than once"
                    else
                        pairs.Add entry
                | at ->
                    // Refused for the same reason a bare value is, and checked here so the
                    // flag form is covered too: `a,b` would otherwise reach YAML as two keys,
                    // which also carries the second one past the duplicate check above.
                    failure <-
                        Some
                            $"the argument name '%s{key}' contains '%c{key[at]}', which YAML reads as punctuation; an argument's name cannot contain , {{ }} [ ] or :"

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
                            // A closing quote has to be the end of the value. Without this,
                            // `title="a"x` leaves the parser sitting on `x`, which is then
                            // read as a fresh key and silently becomes a flag.
                            if next < arguments.Length && not (Char.IsWhiteSpace arguments[next]) then
                                failure <-
                                    Some
                                        $"the value of '%s{key}' has '%c{arguments[next]}' after its closing quote; separate arguments with a space"
                            else
                                add key $"%s{key}: %s{value}"
                                index <- next
                        | Error reason -> failure <- Some reason
                    else
                        let value, next = readBare arguments index

                        match value.IndexOfAny flowPunctuation with
                        | -1 ->
                            add key $"%s{key}: %s{value}"
                            index <- next
                        | at ->
                            failure <-
                                Some
                                    $"""the value of '%s{key}' contains '%c{value[at]}', which YAML reads as punctuation; write it as %s{key}="%s{value}" to keep it as text"""
                else
                    // A key on its own is a flag, which is how a reader writes it anyway.
                    add key $"%s{key}: true"
            end

        match failure with
        | Some reason -> Error reason
        | None -> Ok $"""{{{String.Join(", ", pairs)}}}"""
