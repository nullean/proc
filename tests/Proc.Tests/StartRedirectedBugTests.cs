using System;
using System.Collections.Generic;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class StartRedirectedBugTests : TestsBase
	{
		private class CapturingHandler : IConsoleLineHandler
		{
			public List<LineOut> Lines { get; } = new();
			public void Handle(LineOut line) => Lines.Add(line);
			public void Handle(Exception e) { }
		}

		[Fact]
		public void StartRedirected_WithLineHandlerOverload_DoesNotRecurse()
		{
			// Bug: StartRedirected(IConsoleLineHandler, string, params string[]) calls itself
			// infinitely via params string[] coercion, causing StackOverflowException.
			// Before fix: crashes the test runner process.
			// After fix: routes to StartRedirected(new StartArguments(bin, arguments), lineHandler).
			var handler = new CapturingHandler();
			var result = Proc.StartRedirected(handler, "dotnet", "--version");
			result.Completed.Should().BeTrue();
			result.ExitCode.Should().Be(0);
			handler.Lines.Should().NotBeEmpty("dotnet --version should print at least one line");
		}
	}
}
