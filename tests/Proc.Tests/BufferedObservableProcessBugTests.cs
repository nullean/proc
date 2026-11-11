using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AwesomeAssertions;

namespace ProcNet.Tests
{
	public class BufferedObservableProcessBugTests : TestsBase
	{
		[Test]
		public void ObservableProcess_FastExitingProcess_AlwaysCompletesObservable()
		{
			// Bug (TOCTOU): in BufferedObservableProcess.KickOff(), the process is checked for
			// HasExited before the Exited event handler is registered. If the process exits in
			// that window, the Exited event fires with no handler, OnExit is never called, and
			// WaitForCompletion blocks until its timeout instead of returning true.
			// SingleLine exits almost immediately, making this race reproducible under load.
			// After fix: Exited handler is attached before async reads start, and HasExited is
			// re-checked after attaching.
			var failures = new List<int>();
			for (var i = 0; i < 30; i++)
			{
				var process = new ObservableProcess(TestCaseArguments("SingleLine"));
				process.SubscribeLines(_ => { });
				var completed = process.WaitForCompletion(TimeSpan.FromSeconds(5));
				if (!completed) failures.Add(i);
			}

			failures.Should().BeEmpty(
				"WaitForCompletion should always return true — TOCTOU race prevents OnExit from firing");
		}

		[Test]
		public void ProcStart_NativeFastExitingProcess_DoesNotObserveDisposedProcess()
		{
			// Bug: KickOff() subscribed Process.Exited before StartAsyncReads(). A process that exits in that
			// window raises Exited on a thread pool thread, whose handler runs OnExit -> Stop -> Process.Dispose()
			// while KickOff is still about to touch Process.StandardOutput/StandardError, which then throws
			// ObjectDisposedException or "InvalidOperationException: No process is associated with this object".
			// A native binary that exits within milliseconds hits it; `dotnet <dll>`'s host startup hides the window.
			// After fix: async reads start before Exited is subscribed, so the handler can not dispose the process
			// before the readers hold its streams.
			var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			var failures = new List<string>();
			for (var i = 0; i < 200; i++)
			{
				var args = isWindows
					? new StartArguments("cmd", "/C", "echo", "hi")
					: new StartArguments("echo", "hi");
				args.Timeout = TimeSpan.FromSeconds(10);
				try
				{
					var result = Proc.Start(args);
					if (result.ExitCode != 0) failures.Add($"{i}: exit code {result.ExitCode}");
				}
				catch (Exception e)
				{
					failures.Add($"{i}: {e}");
				}
			}

			failures.Should().BeEmpty("a fast-exiting process should never surface a disposed Process");
		}
	}
}
