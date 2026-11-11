module Proc.Fs.Tests.ShellSyntaxTests

open System
open CsCheck
open Proc.Fs
open TUnit.Core

/// Binary and arguments of each command in a pipe, first command first
let rec private stages (c: Cmd) =
    let upstream =
        match c.Stdin with
        | Some (StdinSource.Upstream u) -> stages u
        | _ -> []
    upstream @ [ c.Binary :: (c.Arguments |> Option.defaultValue []) ]

let private args (c: Cmd) =
    match stages c with
    | [ _ :: args ] -> args
    | other -> failwithf "expected one command, got %A" other

let private eq (expected: 'T) (actual: 'T) = Assert.Equal<'T>(expected, actual)

// CsCheck's .Array[min, max] is a C# extension property, which F# can not see
let private arrayOf (gen: Gen<'T>) min max : Gen<'T array> = GenArray<'T>(gen)[min, max]

// Characters that never mean anything to the parser
let private plainChar = Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.,/=:+@%^*~#"]
let private word = Gen.String[plainChar, 1, 12]
let private words = arrayOf word 1 6
let private separator = Gen.String[Gen.Char[" \t\n"], 1, 3]

let private stdinFile (c: Cmd) =
    let rec first (c: Cmd) =
        match c.Stdin with
        | Some (StdinSource.Upstream u) -> first u
        | Some (StdinSource.File f) -> Some f
        | _ -> None
    first c

// ---- properties ----

type ShellSyntaxTests() =

    [<Test>]
    member _.``any value is exactly one argument`` () =
        Gen.String.Sample(fun (v: string) ->
            let c = sh $"bin {v}"
            eq [ [ "bin"; v ] ] (stages c))

    [<Test>]
    member _.``any value joins the word around it`` () =
        Gen.Select(Gen.String, word, word, fun v p s -> (v, p, s)).Sample(fun (v: string, prefix: string, suffix: string) ->
            eq [ prefix + v + suffix ] (args (sh $"bin {prefix}{v}{suffix}")))

    [<Test>]
    member _.``any value inside quotes stays inside that argument`` () =
        Gen.String.Sample(fun (v: string) ->
            eq [ "pre " + v + " post" ] (args (sh $"bin \"pre {v} post\""))
            eq [ "pre " + v ] (args (sh $"bin 'pre {v}'")))

    [<Test>]
    member _.``a list standing alone adds one argument per item`` () =
        (arrayOf Gen.String 0 5).Sample(fun (items: string array) ->
            let items = List.ofSeq items
            eq ([ "first" ] @ items @ [ "last" ]) (args (sh $"bin first {items} last")))

    [<Test>]
    member _.``plain words split on any whitespace`` () =
        Gen.Select(words, separator, separator, fun w s e -> (w, s, e)).Sample(fun (ws: string array, sep: string, edge: string) ->
            let ws = List.ofSeq ws
            let c = Cmd.ofString (edge + String.Join(sep, ws) + edge)
            eq [ ws ] (stages c))

    [<Test>]
    member _.``single quotes keep any text as one argument`` () =
        Gen.String.Where(fun s -> not (s.Contains "'")).Sample(fun (text: string) ->
            eq [ text ] (args (Cmd.ofString $"bin '{text}'")))

    [<Test>]
    member _.``double quotes keep any text as one argument once quotes and backslashes are escaped`` () =
        Gen.String.Sample(fun (text: string) ->
            let escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"")
            eq [ text ] (args (Cmd.ofString ("bin \"" + escaped + "\""))))

    [<Test>]
    member _.``unquoted pipes split commands`` () =
        (arrayOf words 1 4).Sample(fun (commands: string array array) ->
            let commands = commands |> Seq.map List.ofSeq |> List.ofSeq
            let text = String.Join(" | ", commands |> List.map (fun ws -> String.Join(" ", ws)))
            eq commands (stages (Cmd.ofString text)))

    [<Test>]
    member _.``a value can never add a pipe`` () =
        Gen.String.Sample(fun (v: string) ->
            eq [ [ "a"; v ]; [ "b" ] ] (stages (sh $"a {v} | b")))

    [<Test>]
    member _.``any value is exactly the file name of a redirect`` () =
        Gen.String.Sample(fun (v: string) ->
            let c = sh $"a < {v} | b >> {v}"
            eq [ [ "a" ]; [ "b" ] ] (stages c)
            eq (Some v) (stdinFile c)
            eq (Some (StdoutTarget.Append v)) c.Stdout)

    [<Test>]
    member _.``a value can never add a redirect`` () =
        Gen.String.Sample(fun (v: string) ->
            let c = sh $"a {v}"
            eq None c.Stdout
            eq None c.Stdin)

    // ---- examples ----

    [<Test>]
    member _.``redirects attach to the first and last command`` () =
        let c = Cmd.ofString "sort -r < 'in file.txt' | uniq > out.txt"
        eq [ [ "sort"; "-r" ]; [ "uniq" ] ] (stages c)
        eq (Some "in file.txt") (stdinFile c)
        eq (Some (StdoutTarget.File "out.txt")) c.Stdout
        let c = Cmd.ofString "a >>log.txt b"
        eq [ "b" ] (args c)
        eq (Some (StdoutTarget.Append "log.txt")) c.Stdout
        let files = [ "a"; "b" ]
        Assert.Throws<ArgumentException>(fun () -> sh $"a > {files}" |> ignore) |> ignore


    [<Test>]
    member _.``the README examples parse as documented`` () =
        let file = "my file.fs"
        eq [ [ "wc"; "-l"; "my file.fs" ] ] (stages (sh $"wc -l {file}"))
        let msg = "fix; rm -rf ~"
        eq [ [ "git"; "commit"; "-m"; "fix; rm -rf ~" ] ] (stages (sh $"git commit -m {msg}"))
        let projects = [ "a.csproj"; "b.csproj" ]
        eq [ [ "dotnet"; "test"; "a.csproj"; "b.csproj" ] ] (stages (sh $"dotnet test {projects}"))
        eq [ [ "grep"; "a|b"; "x" ] ] (stages (sh $"grep 'a|b' x"))
        eq [ [ "git"; "ls-files" ]; [ "grep"; "\\.fs$" ]; [ "sort"; "-r" ] ] (stages (sh $"git ls-files | grep \.fs$ | sort -r"))

    [<Test>]
    member _.``empty values and empty quotes are empty arguments`` () =
        let empty = ""
        eq [ ""; "" ; "" ] (args (sh $"bin {empty} '' \"\""))

    [<Test>]
    member _.``an empty list adds no arguments`` () =
        let none: string list = []
        eq [ "a" ] (args (sh $"bin {none} a"))

    [<Test>]
    member _.``formats use the invariant culture`` () =
        let n = 1234.5
        eq [ "1234.50"; "1,234.5" ] (args (sh $"bin {n:F2} {n:N1}"))

    [<Test>]
    member _.``doubled braces are literal braces`` () =
        let name = "proc"
        eq [ "{\"name\": \"proc\"}" ] (args (sh $"bin '{{\"name\": \"{name}\"}}'"))
        eq [ "{\"name\": \"proc\"}" ] (args (sh $$"""bin '{"name": "{{name}}"}'"""))

    [<Test>]
    member _.``multi-line commands and line continuations`` () =
        let dir = "/my dir"
        let c = sh $"""
            docker run --rm \
              -e "CI=true" \
              -v {dir}:/src
              alpine ls
        """
        eq [ [ "docker"; "run"; "--rm"; "-e"; "CI=true"; "-v"; "/my dir:/src"; "alpine"; "ls" ] ] (stages c)

    [<Test>]
    member _.``a backslash not before a newline is kept`` () =
        eq [ "a\\b"; "c\\" ] (args (Cmd.ofString "bin a\\b c\\"))
        eq [ "x\\y" ] (args (Cmd.ofString "bin \"x\\y\""))

    [<Test>]
    member _.``ofString keeps braces literal`` () =
        eq [ "{0}"; "{x}" ] (args (Cmd.ofString "bin {0} {x}"))

    [<Test>]
    member _.``a value can be the binary`` () =
        let tool = "/opt/my tools/run"
        eq [ [ "/opt/my tools/run"; "--version" ] ] (stages (sh $"{tool} --version"))

    [<Test>]
    [<Arguments("")>]
    [<Arguments("   ")>]
    [<Arguments("a |")>]
    [<Arguments("| a")>]
    [<Arguments("a || b")>]
    [<Arguments("a 'b")>]
    [<Arguments("a \"b")>]
    [<Arguments("a >")>]
    [<Arguments("a <")>]
    [<Arguments("a > | b")>]
    [<Arguments("a > out | b")>]
    [<Arguments("a | b < in")>]
    [<Arguments("a > x > y")>]
    [<Arguments("a >> x > y")>]
    [<Arguments("a < x < y")>]
    [<Arguments("a > > b")>]
    [<Arguments("a >>> b")>]
    [<Arguments("a 2> err")>]
    [<Arguments("a>b")>]
    [<Arguments("> out")>]
    [<Arguments("a; b")>]
    [<Arguments("a && b")>]
    [<Arguments("a &")>]
    member _.``invalid commands throw`` (text: string) =
        Assert.Throws<ArgumentException>(fun () -> Cmd.ofString text |> ignore) |> ignore

    [<Test>]
    member _.``quoted special characters are arguments`` () =
        eq [ ">"; "a;b"; "&&"; "<" ] (args (Cmd.ofString "bin '>' 'a;b' \"&&\" '<'"))

    [<Test>]
    member _.``a list inside a word throws`` () =
        let items = [ "a"; "b" ]
        Assert.Throws<ArgumentException>(fun () -> sh $"bin -x{items}" |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> sh $"bin {items}-x" |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> sh $"bin '{items}'" |> ignore) |> ignore

    [<Test>]
    member _.``a null value throws`` () =
        let value: string = null
        Assert.Throws<ArgumentException>(fun () -> sh $"bin {value}" |> ignore) |> ignore
