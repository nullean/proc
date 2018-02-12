using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ObservableProcessTestCases : TestsBase
	{
		[Fact]
		public void SingleLineNoEnter()
		{
			var seen = new List<string>();
			var process = new ObservableProcess(TestCaseArguments(nameof(SingleLineNoEnter)));
			process.SubscribeLines(c=>seen.Add(c.Line));
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be(nameof(SingleLineNoEnter));
		}
		[Fact]
		public void TwoWrites()
		{
			var seen = new List<string>();
			var process = new ObservableProcess(TestCaseArguments(nameof(TwoWrites)));
			process.SubscribeLines(c=>seen.Add(c.Line));
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be($"{nameof(TwoWrites)}{nameof(TwoWrites)}");
		}

		[Fact]
		public void SingleLine()
		{
			var seen = new List<string>();
			var process = new ObservableProcess(TestCaseArguments(nameof(SingleLine)));
			process.SubscribeLines(c=>seen.Add(c.Line), e=>throw e);
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			seen[0].Should().Be(nameof(SingleLine));
		}

		[Fact]
		public void SingleLineNoEnterCharacters()
		{
			var seen = new List<char[]>();
			var process = new ObservableProcess(TestCaseArguments(nameof(SingleLineNoEnter)));
			process.Subscribe(c=>seen.Add(c.Characters));
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			var chars = nameof(SingleLineNoEnter).ToCharArray();
			seen[0].Should()
				.HaveCount(chars.Length)
				.And.ContainInOrder(chars);

		}
		[Fact]
		public void SingleLineCharacters()
		{
			var seen = new List<char[]>();
			var process = new ObservableProcess(TestCaseArguments(nameof(SingleLine)));
			process.Subscribe(c=>seen.Add(c.Characters));
			process.WaitForCompletion(WaitTimeout);

			seen.Should().NotBeEmpty().And.HaveCount(1, string.Join(Environment.NewLine, seen));
			var chars = nameof(SingleLine).ToCharArray()
				.Concat(Environment.NewLine.ToCharArray())
				.ToArray();
			seen[0].Should()
				.HaveCount(chars.Length)
				.And.ContainInOrder(chars);

		}
	}
}
