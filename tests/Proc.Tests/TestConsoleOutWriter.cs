using System;
using System.IO;
using System.Text;
using ProcNet.Std;
using TUnit.Core;

/// <summary>Buffers what a process writes and echoes it to the output of the test that created it</summary>
public class TestConsoleOutWriter : IConsoleOutWriter
{
	// captured up front: the process reports its output on other threads, where TestContext.Current may not flow
	private readonly TextWriter output = (TestContext.Current ??
		throw new InvalidOperationException("Create a TestConsoleOutWriter inside a test")).OutputWriter;
	private readonly StringBuilder _sb = new();
	public string[] Lines => _sb.ToString().Replace("\r\n", "\n").Split(new[] { "\n" }, StringSplitOptions.None);
	public string Text => _sb.ToString();
	private static char[] NewLineChars = Environment.NewLine.ToCharArray();

	public void Write(Exception e) => throw e;

	public void Write(ConsoleOut consoleOut)
	{
		consoleOut.CharsOrString(c => _sb.Append(new string(c)), s => _sb.AppendLine(s));
		consoleOut.CharsOrString(c => output.WriteLine(new string(c).TrimEnd(NewLineChars)), s => output.WriteLine(s));
	}
}
