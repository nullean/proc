using System;
using System.Collections.Generic;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class ProcTestCases : TestsBase
	{
		public class TestConsoleOutWriter : IConsoleOutWriter
		{
			public Exception SeenException { get; private set; }

			public List<ConsoleOut> Out { get; } = new List<ConsoleOut>();

			public void Write(Exception e) => SeenException = e;

			public void Write(ConsoleOut consoleOut) => Out.Add(consoleOut);
		}

		[Fact]
		public void ReadKeyFirst()
		{
			var args = TestCaseArguments(nameof(ReadKeyFirst));
			args.StandardInputHandler = s => s.Write("y");
			var writer = new TestConsoleOutWriter();
			args.ConsoleOutWriter = writer;
			var result = Proc.Start(args);
			result.Completed.Should().BeTrue("completed");
			result.ExitCode.Should().HaveValue();
			result.ConsoleOut.Should().NotBeEmpty();
			writer.Out.Should().NotBeEmpty();
		}

		[Fact]
		public void BadBinary()
		{
			Action call = () => Proc.Start("this-does-not-exist.exe");
			var shouldThrow = call.Should().Throw<ObservableProcessException>();
			shouldThrow.And.InnerException?.Message.Should().NotBeEmpty();
			shouldThrow.And.Message.Should().Contain("this-does-not-exist.exe");
		}
	}
}
