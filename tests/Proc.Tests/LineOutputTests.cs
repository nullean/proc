using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class CharacterOutputTestCases : TestsBase
	{
		[Fact]
		public void OverwriteLines()
		{
			var args = TestCaseArguments(nameof(OverwriteLines));
			var result = Proc.Start(args, WaitTimeout);
		}
	}


	public class LineOutputTestCases : TestsBase
	{
		private static readonly string _expected = @"
Windows IP Configuration


Ethernet adapter Ethernet 2:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Wireless LAN adapter Wi-Fi:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Ethernet adapter Ethernet 3:

   Connection-specific DNS Suffix  . :
   Link-local IPv6 Address . . . . . : fe80::e477:59c0:1f38:6cfa%16
   IPv4 Address. . . . . . . . . . . : 10.2.20.225
   Subnet Mask . . . . . . . . . . . : 255.255.255.0
   Default Gateway . . . . . . . . . : 10.2.20.1

Wireless LAN adapter Local Area Connection* 3:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Wireless LAN adapter Local Area Connection* 4:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Ethernet adapter Bluetooth Network Connection:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :";

		private readonly string[] _expectedLines = _expected.Replace("\r\n", "\n").Split(new [] {"\n"}, StringSplitOptions.None);

		[Fact]
		public void ProcSeesAllLines()
		{
			var args = TestCaseArguments("MoreText");
			var result = Proc.Start(args, WaitTimeout);
			result.ExitCode.Should().HaveValue();
			result.Completed.Should().BeTrue();
			//result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(_expectedLines.Length);
			for (var i = 0; i < result.ConsoleOut.Count; i++)
				result.ConsoleOut[i].Line.Should().Be(_expectedLines[i], i.ToString());
		}

		[Fact]
		public void ProcSeesAllLinesWithoutConsoleOutWriter()
		{
			var args = TestCaseArguments("MoreText");
			var result = Proc.Start(args, WaitTimeout, null);
			result.ExitCode.Should().HaveValue();
			result.Completed.Should().BeTrue();
			//result.ConsoleOut.Should().NotBeEmpty().And.HaveCount(_expectedLines.Length);
			for (var i = 0; i < result.ConsoleOut.Count; i++)
				result.ConsoleOut[i].Line.Should().Be(_expectedLines[i], i.ToString());
		}

		[Fact]
		public void SubscribeLinesSeesAllLines()
		{
			var args = TestCaseArguments("MoreText");
			var o = new ObservableProcess(args);
			var seen = new List<LineOut>();
			o.SubscribeLines(l => seen.Add(l));
			o.WaitForCompletion(WaitTimeout);
			seen.Should().NotBeEmpty().And.HaveCount(_expectedLines.Length, string.Join("\r\n", seen.Select(s=>s.Line)));
			for (var i = 0; i < seen.Count; i++)
				seen[i].Line.Should().Be(_expectedLines[i], i.ToString());
		}
		[Fact]
		public void ConsoleWriterSeesAllLines()
		{
			var writer = new TestConsoleOutWriter();
			var args = TestCaseArguments("MoreText");
			var result = Proc.Start(args, WaitTimeout, writer);
			result.ExitCode.Should().HaveValue();
			result.Completed.Should().BeTrue();
			var lines = writer.Lines;
			lines.Should().NotBeEmpty().And.HaveCount(_expectedLines.Length + 1);
			for (var i = 0; i < lines.Length - 1; i++)
				lines[i].Should().Be(_expectedLines[i], i.ToString());
		}

		public class TestConsoleOutWriter : IConsoleOutWriter
		{
			private readonly StringBuilder _sb = new StringBuilder();
			public string[] Lines => _sb.ToString().Replace("\r\n", "\n").Split(new [] {"\n"}, StringSplitOptions.None);
			public string Text => _sb.ToString();

			public void Write(Exception e) => throw e;

			public void Write(ConsoleOut consoleOut) => consoleOut.CharsOrString(c=>_sb.Append(new string(c)), s=>_sb.AppendLine(s));
		}
	}
}
