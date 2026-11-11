using AwesomeAssertions;

namespace ProcNet.Tests;

public class PrintArgsTests : TestsBase
{
	[Test]
	public void ProcSendsAllArguments()
	{
		string[] testArgs = ["hello", "world"];
		AssertOutput(testArgs);
	}

#if NETSTANDARD2_1
	[Test]
	public void ArgumentsWithSpaceAreNotSplit()
	{
		string[] testArgs = ["hello", "world", "this argument has spaces"];
		AssertOutput(testArgs);
	}

	[Test]
	public void ArgumentsSeesArgumentsAfterQuoted()
	{
		string[] testArgs = ["this argument has spaces", "hello", "world"];
		AssertOutput(testArgs);
	}
	[Test]
	public void EscapedQuotes()
	{
		string[] testArgs = ["\"this argument has spaces\"", "hello", "world"];
		AssertOutput(testArgs);
	}

#else
	[Test]
	public void EscapedQuotes()
	{
		string[] testArgs = ["\"this argument has spaces\"", "hello", "world"];
		var args = TestCaseArguments("PrintArgs", testArgs);
		var outputWriter = new TestConsoleOutWriter();
		args.ConsoleOutWriter = outputWriter;
		var result = Proc.Start(args);
		result.ExitCode.Should().Be(0);
		result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(testArgs.Length);
	}
#endif

	private void AssertOutput(string[] testArgs)
	{
		var args = TestCaseArguments("PrintArgs", testArgs);
		var outputWriter = new TestConsoleOutWriter();
		args.ConsoleOutWriter = outputWriter;
		var result = Proc.Start(args);
		result.ExitCode.Should().Be(0);
		result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(testArgs.Length);
		for (var i = 0; i < result.ConsoleOut.Count; i++)
			result.ConsoleOut[i].Line.Should().Be(testArgs[i], i.ToString());
	}

}
