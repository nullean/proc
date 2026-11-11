/// The handful of xUnit-style assertions these tests use, on AwesomeAssertions.
/// F# can not pick between AwesomeAssertions' many Should() overloads for a generic value, so the tests call these
/// instead; each keeps the meaning of the xUnit assertion it replaced (Throws is exact-type, Equal is Equals).
namespace Proc.Fs.Tests

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open AwesomeAssertions

// AwesomeAssertions names the subject of a failure by reading the source line that called Should(). Not inlining
// these keeps that line in this file, where it names the subject, instead of an F# test line it can not parse.
[<AbstractClass; Sealed>]
type Assert =
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Equal<'T>(expected: 'T, actual: 'T) =
        let actual = box actual
        actual.Should().Be(box expected) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member NotEqual<'T>(expected: 'T, actual: 'T) =
        let actual = box actual
        actual.Should().NotBe(box expected) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member True(condition: bool) = condition.Should().BeTrue() |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member True(condition: bool, because: string) = condition.Should().BeTrue(because) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member False(condition: bool) = condition.Should().BeFalse() |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member False(condition: bool, because: string) = condition.Should().BeFalse(because) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Contains(expectedSubstring: string, actual: string) =
        actual.Should().Contain(expectedSubstring) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member DoesNotContain(expectedSubstring: string, actual: string) =
        actual.Should().NotContain(expectedSubstring) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member EndsWith(expectedEndString: string, actual: string) =
        actual.Should().EndWith(expectedEndString) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member NotEmpty(collection: IEnumerable<'T>) =
        collection.Should().NotBeEmpty() |> ignore

    static member All(collection: IEnumerable<'T>, action: Action<'T>) =
        for item in collection do action.Invoke item

    /// Like xUnit's Assert.Throws: the exception must be exactly 'E, not a subclass
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Throws<'E when 'E :> exn>(testCode: Action) : 'E =
        testCode.Should().ThrowExactly<'E>().Which
