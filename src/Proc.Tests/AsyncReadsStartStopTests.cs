using System;
using System.Collections.Generic;
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
			process.SubscribeLines(c =>
			{
				consoleOut.Add(c);
				if (!c.Line.EndsWith("3")) return;

				process.CancelAsyncReads();
				Task.Run(async () =>
				{
					await Task.Delay(TimeSpan.FromSeconds(2));
					process.StartAsyncReads();
				});
			}, e=> seenException = e);

			process.WaitForCompletion(TimeSpan.FromSeconds(20));

			process.ExitCode.Should().HaveValue().And.Be(121);
			seenException.Should().BeNull();
			consoleOut.Should().NotBeEmpty()
				//we stopped reads after 3 or 2 seconds
				.And.NotContain(l => l.Line.EndsWith("4"))
				//each line is delayed 500ms so after 2 seconds
				//and subscribing again we should see 9
				.And.Contain(l => l.Line.EndsWith("9"));
		}
	}
}
