using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ProcNet.Tests
{
	public class LongRunningTests(ITestOutputHelper output) : TestsBase
	{
		[Fact]
		public async Task LongRunningShouldSeeAllOutput()
		{
			var args = LongRunningTestCaseArguments("LongRunning");
			args.StartedConfirmationHandler = l => l.Line == "Started!";

			var outputWriter = new TestConsoleOutWriter(output);

			using (var process = Proc.StartLongRunning(args, WaitTimeout, outputWriter))
			{
				process.Running.Should().BeTrue();
				await Task.Delay(TimeSpan.FromSeconds(2));
				process.Running.Should().BeFalse();
			}

			var lines = outputWriter.Lines;
			lines.Length.Should().BeGreaterThan(0);
			lines.Should().Contain(s => s.StartsWith("Starting up:"));
			lines.Should().Contain(s => s == "Started!");
			lines.Should().Contain(s => s.StartsWith("Data after startup:"));
		}

		[Fact]
		public async Task LongRunningShouldStopBufferingOutputWhenAsked()
		{
			var args = LongRunningTestCaseArguments("TrulyLongRunning");
			args.StartedConfirmationHandler = l => l.Line == "Started!";
			args.StopBufferingAfterStarted = true;

			var outputWriter = new TestConsoleOutWriter(output);
			var sw = Stopwatch.StartNew();

			using (var process = Proc.StartLongRunning(args, WaitTimeout, outputWriter))
			{
				process.Running.Should().BeTrue();
				sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
				var lines = outputWriter.Lines;
				lines.Length.Should().BeGreaterThan(0);
				lines.Should().Contain(s => s.StartsWith("Starting up:"));
				lines.Should().Contain(s => s == "Started!");
				lines.Should().NotContain(s => s.StartsWith("Data after startup:"));
				await Task.Delay(TimeSpan.FromSeconds(2));
				lines.Should().NotContain(s => s.StartsWith("Data after startup:"));
			}

			// we dispose before the program's completion
			sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));

		}

		[Fact]
		public async Task LongRunningWithoutConfirmationHandler()
		{
			var args = LongRunningTestCaseArguments("LongRunning");
			var outputWriter = new TestConsoleOutWriter(output);

			using (var process = Proc.StartLongRunning(args, WaitTimeout, outputWriter))
			{
				process.Running.Should().BeTrue();
				await Task.Delay(TimeSpan.FromSeconds(2));
			}

			var lines = outputWriter.Lines;
			lines.Should().Contain(s => s.StartsWith("Starting up:"));
			lines.Should().Contain(s => s == "Started!");
			lines.Should().Contain(s => s.StartsWith("Data after startup:"));
			lines.Length.Should().BeGreaterThan(0);
		}
	}
}
