using System;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class StartLongRunningBugTests : TestsBase
	{
		[Fact]
		public void StartLongRunning_Dispose_AlsoDisposesWaitHandle()
		{
			// Bug: LongRunningApplicationSubscription.Dispose() disposes the subscription and
			// process, but never calls WaitHandle.Dispose(). WaitHandle is a ManualResetEvent
			// (OS handle) and leaks a kernel object on each disposal.
			// After fix: Dispose() calls WaitHandle.Dispose().
			// Observable effect: WaitOne() on a disposed WaitHandle throws ObjectDisposedException.
			var args = LongRunningTestCaseArguments("LongRunning");
			var subscription = Proc.StartLongRunning(args, WaitTimeout);
			subscription.Dispose();

			// Before fix: WaitHandle is still live — WaitOne(0) returns immediately (false), no exception.
			// After fix: WaitHandle is disposed — WaitOne(0) throws ObjectDisposedException.
			Action use = () => subscription.WaitHandle.WaitOne(0);
			use.Should().Throw<ObjectDisposedException>(
				"WaitHandle should be disposed when the subscription is disposed");
		}
	}
}
