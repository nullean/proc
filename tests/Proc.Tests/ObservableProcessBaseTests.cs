using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ObservableProcessBaseTests : TestsBase
	{
		[Fact]
		public void ObservableProcess_ConcurrentExitAndDispose_DoesNotThrow()
		{
			// Bug (#8): Dispose() called Stop() without holding _exitLock, while the process
			// Exited event called ExitStop() which does hold _exitLock. Both could enter Stop()
			// concurrently, racing on Started, ExitCode, and other shared state.
			// SingleLine exits almost immediately, maximising the chance of hitting the race.
			// After fix: Dispose() also acquires _exitLock before calling Stop().
			var exceptions = new List<Exception>();
			for (var i = 0; i < 30; i++)
			{
				try
				{
					var process = new ObservableProcess(TestCaseArguments("SingleLine"));
					process.SubscribeLines(_ => { });
					Thread.Sleep(10); // give process a chance to start and exit
					process.Dispose();
				}
				catch (Exception e)
				{
					exceptions.Add(e);
				}
			}

			exceptions.Should().BeEmpty("concurrent exit and dispose should not race into Stop()");
		}
	}
}
