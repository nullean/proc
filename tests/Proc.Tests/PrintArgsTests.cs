using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ProcNet.Tests;

public class PrintArgsTests(ITestOutputHelper output) : TestsBase
{
	[Fact]
	public void ProcSendsAllArguments()
	{
		string[] testArgs = ["hello", "world"];
		AssertOutput(testArgs);
	}

	[Fact]
	public void ArgumentsWithSpaceAreNotSplit()
	{
		string[] testArgs = ["hello", "world", "this argument has spaces"];
		AssertOutput(testArgs);
	}

	[Fact]
	public void ArgumentsSeesArgumentsAfterQuoted()
	{
		string[] testArgs = ["this argument has spaces", "hello", "world"];
		AssertOutput(testArgs);
	}
	[Fact]
	public void EscapedQuotes()
	{
		string[] testArgs = ["\"this argument has spaces\"", "hello", "world"];
		AssertOutput(testArgs);
	}

	private void AssertOutput(string[] testArgs)
	{
		var args = TestCaseArguments("PrintArgs", testArgs);
		var outputWriter = new TestConsoleOutWriter(output);
		args.ConsoleOutWriter = outputWriter;
		var result = Proc.Start(args);
		result.ExitCode.Should().Be(0);
		result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(testArgs.Length);
		for (var i = 0; i < result.ConsoleOut.Count; i++)
			result.ConsoleOut[i].Line.Should().Be(testArgs[i], i.ToString());
	}

}
