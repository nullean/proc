using System;
using AwesomeAssertions;

namespace ProcNet.Tests;

public class ReadInOrderTests : TestsBase
{
	[Test]
	public void InterMixedOutAndError()
	{
		var args = TestCaseArguments(nameof(InterMixedOutAndError));
		args.Timeout = TimeSpan.FromSeconds(10); // Allow more time for 200 lines with 1ms delays
		var result = Proc.Start(args);
		result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(200);

		var interspersed = 0;
		bool? previous = null;
		foreach (var o in result.ConsoleOut)
		{
			if (previous.HasValue && previous.Value != o.Error)
				interspersed++;
			previous = o.Error;
		}

		interspersed.Should().BeGreaterThanOrEqualTo(3);
	}
}
