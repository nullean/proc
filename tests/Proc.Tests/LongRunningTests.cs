using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace ProcNet.Tests;

public class LongRunningTests : TestsBase
{
	[Test]
	public async Task LongRunningShouldSeeAllOutput()
	{
		var args = LongRunningTestCaseArguments("LongRunning");
		args.StartedConfirmationHandler = l => l.Line == "Started!";

		var outputWriter = new TestConsoleOutWriter();

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

	[Test]
	public async Task LongRunningShouldStopBufferingOutputWhenAsked()
	{
		var args = LongRunningTestCaseArguments("TrulyLongRunning");
		args.StartedConfirmationHandler = l => l.Line == "Started!";
		args.StopBufferingAfterStarted = true;

		var outputWriter = new TestConsoleOutWriter();
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

	[Test]
	public async Task LongRunningWithoutConfirmationHandler()
	{
		var args = LongRunningTestCaseArguments("LongRunning");
		var outputWriter = new TestConsoleOutWriter();

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

	[Test]
	public void LongRunningStopsTheProcessWhenStartupIsNotConfirmedInTime()
	{
		var pidFile = Path.Combine(Path.GetTempPath(), $"procnet-{Guid.NewGuid():N}.pid");
		// Writes its PID, then sleeps 30s without printing the line the handler waits for
		var args = new LongRunningArguments(GetTestBinaryPath(), "WritePidAndWait", pidFile)
		{
			WorkingDirectory = GetWorkingDir(),
			StartedConfirmationHandler = _ => false
		};

		Action call = () => Proc.StartLongRunning(args, TimeSpan.FromSeconds(1));
		call.Should().Throw<ProcExecException>().WithMessage("*Could not yield started confirmation*");

		// Give the OS a moment to register the kill
		Thread.Sleep(300);

		File.Exists(pidFile).Should().BeTrue("the child process should have written its PID before sleeping");
		var pid = int.Parse(File.ReadAllText(pidFile));
		File.Delete(pidFile);

		Action check = () => Process.GetProcessById(pid);
		check.Should().Throw<ArgumentException>("the process should be stopped when its startup is not confirmed in time");
	}
}
