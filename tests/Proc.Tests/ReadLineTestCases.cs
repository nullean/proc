using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ReadLineTestCases : TestsBase
	{
		[Fact]
		public void ReadKeyFirst()
		{
			var seen = new List<string>();
			var process = new ObservableProcess(TestCaseArguments(nameof(ReadKeyFirst)));
			process.ProcessStarted += (standardInput) =>
			{
				//this particular program does not output anything and expect user input immediatly
				//OnNext on the observable is only called on output so we need to write on the started event
				process.StandardInput.Write("y");
			};
			process.SubscribeLines(c=>seen.Add(c.Line));
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be($"y{nameof(ReadKeyFirst)}");
		}
		[Fact]
		public void ReadKeyAfter()
		{
			var seen = new List<string>();
			var process = new ObservableProcess(TestCaseArguments(nameof(ReadKeyAfter)));
			process.Subscribe(c=>
			{
				var chars = new string(c.Characters);
				seen.Add(chars);
				if (chars == "input:") process.StandardInput.Write("y");
			});
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(2, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be($"input:");
			seen[1].Should().Be(nameof(ReadKeyAfter));
		}

	}
}
