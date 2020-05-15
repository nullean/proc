using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class TrailingNewLineTestCases : TestsBase
	{
		private static readonly string _expected = @"Windows IP Configuration


";
		private readonly string[] _expectedLines = _expected.Split(Environment.NewLine.ToCharArray());

		[Fact]
		public void ProcSeesAllLines()
		{
			var args = TestCaseArguments("TrailingLines");
			var result = Proc.Start(args, WaitTimeout);
			result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(_expected.Count(c=>c=='\n'));
			for (var i = 0; i < result.ConsoleOut.Count; i++)
				result.ConsoleOut[i].Line.Should().Be(_expectedLines[i], i.ToString());
		}

		[Fact]
		public void SubscribeLinesSeesAllLines()
		{
			var args = TestCaseArguments("TrailingLines");
			var o = new ObservableProcess(args);
			var seen = new List<LineOut>();
			o.SubscribeLines(l => seen.Add(l));
			o.WaitForCompletion(WaitTimeout);
			seen.Should().NotBeEmpty().And.HaveCount(_expected.Count(c=>c=='\n'));
			for (var i = 0; i < seen.Count; i++)
				seen[i].Line.Should().Be(_expectedLines[i], i.ToString());
		}
		[Fact]
		public void ConsoleWriterSeesAllLines()
		{
			var writer = new TestConsoleOutWriter();
			var args = TestCaseArguments("TrailingLines");
			var result = Proc.Start(args, WaitTimeout, writer);
			var lines = writer.Lines;
			lines.Should().NotBeEmpty().And.HaveCount(_expectedLines.Length);
			for (var i = 0; i < lines.Length; i++)
				lines[i].Should().Be(_expectedLines[i], i.ToString());
		}

		public class TestConsoleOutWriter : IConsoleOutWriter
		{
			private readonly StringBuilder _sb = new StringBuilder();
			public string[] Lines => _sb.ToString().Split(Environment.NewLine.ToCharArray());
			public string Text => _sb.ToString();

			public void Write(Exception e) => throw e;

			public void Write(ConsoleOut consoleOut) => consoleOut.CharsOrString(c=>_sb.Append(new string(c)), s=>_sb.AppendLine(s));
		}
	}
}
