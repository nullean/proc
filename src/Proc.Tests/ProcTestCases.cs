using System;
using System.Collections.Generic;
using FluentAssertions;
using Proc.Std;
using Xunit;

namespace Proc.Tests
{
	public class ProcTestCases : TestsBase
	{
		public class TestConsoleOutWriter : IConsoleOutWriter
		{
			public Exception SeenException { get; private set; }

			public List<ConsoleOut> Out { get; } = new List<ConsoleOut>();

			public void Write(Exception e) => this.SeenException = e;

			public void Write(ConsoleOut consoleOut) => this.Out.Add(consoleOut);
		}

		[Fact]
		public void ReadKeyFirst()
		{
			var args = TestCaseArguments(nameof(ReadKeyFirst));
			var writer = new TestConsoleOutWriter();
			var result = global::Proc.Proc.Start(args, WaitTimeout, writer, s => s.Write("y"));
			result.Completed.Should().BeTrue("completed");
			result.ExitCode.Should().HaveValue();
			result.ConsoleOut.Should().NotBeEmpty();
			writer.Out.Should().NotBeEmpty();
		}

		[Fact]
		public void BadBinary()
		{
			//Proc throws exceptions where as the observable does not.
			var writer = new TestConsoleOutWriter();
			Action call = () => global::Proc.Proc.Start("this-does-not-exist.exe");
			var shouldThrow = call.ShouldThrow<ObservableProcessException>();
			shouldThrow.And.InnerException.Message.Should().Contain("The system cannot");
			shouldThrow.And.Message.Should().Contain("this-does-not-exist.exe");
		}
	}
}
