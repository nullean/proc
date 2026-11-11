/// Splits shell-like command text such as <c>git ls-files | grep {pattern}</c> into binaries and arguments.
/// No shell ever runs: the result is plain argument lists, so values can not inject commands.
module internal Proc.Fs.ShellSyntax

open System
open System.Collections
open System.Globalization
open System.Text

/// A piece of the command: text the author wrote, or a value from an interpolation placeholder
type Segment =
    | Text of string
    /// The value and its format suffix, such as ",5:N2" from {0,5:N2}
    | Value of value: obj * spec: string

let private fail (command: string) (reason: string) =
    raise (ArgumentException($"Can not parse command \"{command.Trim()}\": {reason}", "command"))

/// Splits a FormattableString format such as "wc -l {0}" into text and values
let segmentsOf (format: string) (args: obj array) =
    let segments = ResizeArray<Segment>()
    let text = StringBuilder()
    let flush () =
        if text.Length > 0 then
            segments.Add(Text (text.ToString()))
            text.Clear() |> ignore
    let mutable i = 0
    while i < format.Length do
        let c = format[i]
        let next = if i + 1 < format.Length then Some format[i + 1] else None
        match c, next with
        | '{', Some '{' -> text.Append '{' |> ignore; i <- i + 2
        | '}', Some '}' -> text.Append '}' |> ignore; i <- i + 2
        | '{', _ ->
            let close = format.IndexOf('}', i)
            if close < 0 then fail format "unclosed placeholder"
            let item = format.Substring(i + 1, close - i - 1)
            let split = item.IndexOfAny([| ','; ':' |])
            let index = if split < 0 then item else item.Substring(0, split)
            let spec = if split < 0 then "" else item.Substring split
            flush ()
            segments.Add(Value (args[Int32.Parse(index, CultureInfo.InvariantCulture)], spec))
            i <- close + 1
        | _ ->
            text.Append c |> ignore
            i <- i + 1
    flush ()
    List.ofSeq segments

let private formatValue (command: string) (value: obj) (spec: string) =
    match value with
    | null -> fail command "a placeholder value is null; pass a list to add zero or more arguments"
    | :? string as s when spec = "" -> s
    | v -> String.Format(CultureInfo.InvariantCulture, "{0" + spec + "}", v)

/// The commands of a pipe plus the files its first command reads and its last command writes
type Parsed = {
    Stages: string list list
    /// <c>&lt; file</c> on the first command
    StdinFile: string option
    /// <c>&gt; file</c>, or <c>&gt;&gt; file</c> when the flag is true, on the last command
    StdoutFile: (string * bool) option
}

type private Redirect = In | Out | Append

let private isList (value: obj) =
    match value with
    | :? string -> false
    | :? IEnumerable -> true
    | _ -> false

/// <summary>
/// Splits the segments into one argument list per piped command.
/// <para>Text splits on whitespace; <c>'…'</c> and <c>"…"</c> group; an unquoted <c>|</c> starts the next command.</para>
/// <para>A value always lands inside one argument, never split. A list value standing alone becomes several.</para>
/// <para><c>&lt; file</c> on the first command, <c>&gt; file</c> or <c>&gt;&gt; file</c> on the last one redirect.</para>
/// </summary>
let parse (command: string) (segments: Segment list) : Parsed =
    let stages = ResizeArray<string list>()
    let args = ResizeArray<string>()
    let token = StringBuilder()
    let mutable hasToken = false
    let mutable pendingList: string list option = None
    let mutable quote: char option = None
    let mutable escape = false   // a backslash inside double quotes
    let mutable continuation = false   // an unquoted backslash, which only means something before a newline
    let mutable redirect: Redirect option = None   // a '<', '>' or '>>' still waiting for its file name
    let mutable justRedirected = false   // the previous character was the '>' of a redirect
    let mutable stdinFile: string option = None
    let mutable stdoutFile: (string * bool) option = None

    let noPendingList () =
        if pendingList.IsSome then fail command "a list placeholder must be a whole argument on its own"
    let append (s: string) =
        noPendingList ()
        token.Append s |> ignore
        hasToken <- true
    let endToken () =
        match redirect, pendingList with
        | Some _, Some _ -> fail command "a list placeholder can not be a file name"
        | Some r, None when hasToken ->
            let file = token.ToString()
            match r with
            | In -> stdinFile <- Some file
            | Out -> stdoutFile <- Some (file, false)
            | Append -> stdoutFile <- Some (file, true)
            redirect <- None
        | Some _, None -> ()   // whitespace between '>' and the file name
        | None, Some items -> args.AddRange items
        | None, None -> if hasToken then args.Add(token.ToString())
        token.Clear() |> ignore
        hasToken <- false
        pendingList <- None
    let endStage () =
        if redirect.IsSome then fail command "a '<' or '>' has no file name after it"
        if args.Count = 0 then fail command (if stages.Count = 0 then "it is empty" else "a pipe has an empty command")
        stages.Add(List.ofSeq args)
        args.Clear()
    let flushContinuation () =
        if continuation then
            continuation <- false
            append "\\"

    for segment in segments do
        match segment with
        | Value (value, spec) ->
            flushContinuation ()
            if escape then
                escape <- false
                append "\\"
            justRedirected <- false
            if isList value then
                if quote.IsSome || hasToken || pendingList.IsSome then
                    fail command "a list placeholder must be a whole argument on its own"
                pendingList <- Some [ for item in (value :?> IEnumerable) -> formatValue command item spec ]
            else append (formatValue command value spec)
        | Text text ->
            for c in text do
                let afterRedirect = justRedirected
                justRedirected <- false
                match quote with
                | Some '\'' ->
                    if c = '\'' then quote <- None else append (string c)
                | Some _ ->
                    if escape then
                        escape <- false
                        if c = '"' || c = '\\' then append (string c) else append ("\\" + string c)
                    elif c = '\\' then escape <- true
                    elif c = '"' then quote <- None
                    else append (string c)
                | None when continuation ->
                    continuation <- false
                    // "\<newline>" continues the command on the next line, as in bash
                    if c = '\n' || c = '\r' then endToken ()
                    else
                        append "\\"
                        if Char.IsWhiteSpace c then endToken () else append (string c)
                | None ->
                    match c with
                    | c when Char.IsWhiteSpace c -> endToken ()
                    | '|' ->
                        endToken ()
                        endStage ()
                        if stdoutFile.IsSome then fail command "'>' only works on the last command of a pipe"
                    | '>' when afterRedirect && redirect = Some Out && not hasToken ->
                        redirect <- Some Append
                    | '>' | '<' when hasToken || pendingList.IsSome ->
                        fail command $"write a space before '{c}'; redirecting standard error, as in 2>, is not supported"
                    | '>' | '<' when redirect.IsSome -> fail command "a '<' or '>' has no file name after it"
                    | '<' when stages.Count > 0 -> fail command "'<' only works on the first command of a pipe"
                    | '<' when stdinFile.IsSome -> fail command "only one '<' is allowed"
                    | '>' when stdoutFile.IsSome -> fail command "only one '>' is allowed"
                    | '<' -> redirect <- Some In
                    | '>' ->
                        redirect <- Some Out
                        justRedirected <- true
                    | '\'' | '"' ->
                        noPendingList ()
                        quote <- Some c
                        hasToken <- true
                    | '\\' -> continuation <- true
                    | ';' | '&' ->
                        fail command $"'{c}' is not supported; quote it to pass it as an argument, or put the next command on a new line"
                    | c -> append (string c)

    if quote.IsSome then fail command "a quote is not closed"
    if escape then append "\\"
    flushContinuation ()
    endToken ()
    endStage ()
    { Stages = List.ofSeq stages; StdinFile = stdinFile; StdoutFile = stdoutFile }
