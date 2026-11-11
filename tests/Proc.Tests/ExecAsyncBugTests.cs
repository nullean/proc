using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace ProcNet.Tests;

public class ExecAsyncBugTests : TestsBase
{
	[Test]
	public async Task ExecAsync_WithTimeout_CancellationTokenIsRespected()
	{
		// Bug: timeout branch uses synchronous WaitForExit(int) which blocks the thread
		// and completely ignores the CancellationToken. Cancelling ctx has no effect
		// until the full 30-second timeout elapses.
		// After fix: linked CTS cancels WaitForExitAsync at 500ms.
		var args = ExecTestCaseArguments("SlowOutput"); // runs for ~5s
		args.Timeout = TimeSpan.FromSeconds(30);

		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

		var sw = Stopwatch.StartNew();
		Func<Task> exec = () => Proc.ExecAsync(args, cts.Token);
		await exec.Should().ThrowAsync<OperationCanceledException>();
		sw.Stop();

		sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the CancellationToken should abort the wait, not block for the full 30s timeout");
	}

	[Test]
	public async Task ExecAsync_OnCancellation_KillsChildProcess()
	{
		// Bug: on OperationCanceledException the using-block disposes the Process handle,
		// but Process.Dispose() does not kill the child — it becomes an orphan.
		// After fix: process.Kill(entireProcessTree: true) is called on cancellation.
		var pidFile = Path.Combine(Path.GetTempPath(), $"procnet-{Guid.NewGuid():N}.pid");
		if (File.Exists(pidFile))
			File.Delete(pidFile);

		var args = ExecTestCaseArguments("WritePidAndWait", pidFile); // writes PID then sleeps 30s
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

		Func<Task> exec = () => Proc.ExecAsync(args, cts.Token);
		await exec.Should().ThrowAsync<OperationCanceledException>();

		// Give the OS a moment to register the kill
		await Task.Delay(300);

		File.Exists(pidFile).Should().BeTrue("the child process should have written its PID before sleeping");
		var pid = int.Parse(await File.ReadAllTextAsync(pidFile));

		Action check = () => Process.GetProcessById(pid);
		check.Should().Throw<ArgumentException>("the child process should be dead after cancellation");
	}
}
