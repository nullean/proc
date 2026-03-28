using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class BufferedObservableProcessBugTests : TestsBase
	{
		[Fact]
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
	}
}
