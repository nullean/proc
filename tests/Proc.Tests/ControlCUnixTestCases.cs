#if NET11_0_OR_GREATER
using System;
using System.Collections.Generic;
using FluentAssertions;

namespace ProcNet.Tests
{
	/// <summary>
	/// On .NET 11+, <see cref="ObservableProcessBase{TConsoleOut}.SendControlC(int)"/> delivers SIGINT on
	/// non-Windows platforms via <c>SafeProcessHandle.Signal</c> instead of shelling out to the `kill` binary.
	/// These mirror <see cref="ControlCTestCases"/>, which only runs on Windows.
	/// </summary>
	public class ControlCUnixTestCases : TestsBase
	{
		[SkipOnWindowsFact]
		public void ControlC()
		{
			var args = TestCaseArguments(nameof(ControlC));
			args.SendControlCFirst = true;

			var process = new ObservableProcess(args);
			var seen = new List<string>();
			process.SubscribeLines(c => seen.Add(c.Line));
			process.WaitForCompletion(TimeSpan.FromSeconds(5));

			seen.Should().NotBeEmpty().And.HaveCount(2, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be("Written before control+c");
			seen[1].Should().Be("Written after control+c");
		}

		[SkipOnWindowsFact]
		public void ControlCSend()
		{
			var args = TestCaseArguments(nameof(ControlC));
			args.SendControlCFirst = true;

			var process = new ObservableProcess(args);
			var seen = new List<string>();
			process.SubscribeLines(c =>
			{
				seen.Add(c.Line);
				if (c.Line.Contains("before")) process.SendControlC();
			});
			process.WaitForCompletion(TimeSpan.FromSeconds(5));

			seen.Should().NotBeEmpty().And.HaveCount(2, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be("Written before control+c");
			seen[1].Should().Be("Written after control+c");
		}
	}
}
#endif
