using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ReadInOrderTests : TestsBase
	{
		[Fact]
		public void InterMixedOutAndError()
		{
			var args = TestCaseArguments(nameof(InterMixedOutAndError));
			var result = Proc.Start(args, WaitTimeout);
			result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(200);

			var interspersed = 0;
			bool? previous = null;
			foreach (var o in result.ConsoleOut)
			{
				if (previous.HasValue && previous.Value != o.Error)
					interspersed++;
				previous = o.Error;
			}

			interspersed.Should().BeGreaterOrEqualTo(10);
		}
	}
}
