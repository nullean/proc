using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ControlCTestCases : TestsBase
	{
		[Fact] public void ControlC()
		{
			var args = TestCaseArguments(nameof(ControlC));
			args.SendControlCFirst = true;

			var process = new ObservableProcess(args);
			var seen = new List<string>();
			process.SubscribeLines(c=>
			{
				seen.Add(c.Line);
			});
			process.WaitForCompletion(TimeSpan.FromSeconds(1));

			seen.Should().NotBeEmpty().And.HaveCount(2, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be("Written before control+c");
			seen[1].Should().Be("Written after control+c");
		}
		[Fact] public void ControlCSend()
		{
			var args = TestCaseArguments(nameof(ControlC));
			args.SendControlCFirst = true;

			var process = new ObservableProcess(args);
			var seen = new List<string>();
			process.SubscribeLines(c=>
			{
				seen.Add(c.Line);
				if (c.Line.Contains("before")) process.SendControlC();
			});
			process.WaitForCompletion(TimeSpan.FromSeconds(1));

			seen.Should().NotBeEmpty().And.HaveCount(2, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be("Written before control+c");
			seen[1].Should().Be("Written after control+c");
		}
		[Fact]
		public void ControlCNoWait()
		{
			var args = TestCaseArguments(nameof(ControlCNoWait));
			args.SendControlCFirst = true;

			var process = new ObservableProcess(args);

			var seen = new List<string>();
			process.SubscribeLines(c=>
			{
				seen.Add(c.Line);
			});
			process.WaitForCompletion(TimeSpan.FromSeconds(1));

			//process exits before its control c handler is invoked
			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be("Written before control+c");
		}

	}
}
