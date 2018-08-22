using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class AsyncReadsStartStopTests : TestsBase
	{
		[Fact]
		public void SlowOutput()
		{
			var args = TestCaseArguments(nameof(SlowOutput));
			var process = new ObservableProcess(args);
			var consoleOut = new List<LineOut>();
			Exception seenException = null;
			bool? readingBeforeCancelling = null;
			bool? readingAfterCancelling = null;
			process.SubscribeLines(c =>
			{
				consoleOut.Add(c);
				if (!c.Line.EndsWith("3")) return;

				readingBeforeCancelling = process.IsActivelyReading;

				process.CancelAsyncReads();
				Task.Run(async () =>
				{
					await Task.Delay(TimeSpan.FromSeconds(2));

					readingAfterCancelling = process.IsActivelyReading;
					process.StartAsyncReads();

				});
			}, e=> seenException = e);

			process.WaitForCompletion(TimeSpan.FromSeconds(20));

			process.ExitCode.Should().HaveValue().And.Be(121);
			seenException.Should().BeNull();
			consoleOut.Should().NotBeEmpty()
				.And.Contain(l => l.Line.EndsWith("9"));

			readingBeforeCancelling.Should().HaveValue().And.BeTrue();
			readingAfterCancelling.Should().HaveValue().And.BeFalse();
		}
	}

}
