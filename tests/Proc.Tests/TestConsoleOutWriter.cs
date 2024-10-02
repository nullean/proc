using System;
using System.Text;
using ProcNet.Std;
using Xunit.Abstractions;

public class TestConsoleOutWriter(ITestOutputHelper output) : IConsoleOutWriter
{
	private readonly StringBuilder _sb = new();
	public string[] Lines => _sb.ToString().Replace("\r\n", "\n").Split(new [] {"\n"}, StringSplitOptions.None);
	public string Text => _sb.ToString();
	private static char[] NewLineChars = Environment.NewLine.ToCharArray();

	public void Write(Exception e) => throw e;

	public void Write(ConsoleOut consoleOut)
	{
		consoleOut.CharsOrString(c => _sb.Append(new string(c)), s => _sb.AppendLine(s));
		consoleOut.CharsOrString(c => output.WriteLine(new string(c).TrimEnd(NewLineChars)), s => output.WriteLine(s));
	}
}
