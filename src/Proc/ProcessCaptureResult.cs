using System.Collections.Generic;
using System.Text;
using ProcNet.Std;

namespace ProcNet
{
	/// <summary>
	/// A version of <see cref="ProcessResult"/> that captures all the console out and returns it on <see cref="ConsoleOut"/>
	/// This is handy for when calling small utility binaries but if you are calling a binary with very verbose output you might not
	/// want to capture and store this information.
	/// <para><see cref="Proc.Start(string,string[])"/> and overloads will return a capturing result like this</para>
	/// <para><see cref="Proc.StartRedirected(ProcNet.Std.IConsoleLineHandler,string,string[])"/> and overloads will not return the console out</para>
	/// </summary>
	public class ProcessCaptureResult : ProcessResult
	{
		public IList<LineOut> ConsoleOut { get; }

		public ProcessCaptureResult(bool completed, IList<LineOut> consoleOut, int? exitCode) : base(completed, exitCode) =>
			ConsoleOut = consoleOut ?? new List<LineOut>();

		public override string ToString()
		{
			var sb = new StringBuilder();
			sb.AppendLine(base.ToString());
			sb.AppendLine(new string('-', 8));
			foreach (var o in ConsoleOut)
			{
				sb.AppendLine($"[std{(o.Error ? "err" : "out")}]: {o.Line}");
			}
			return sb.ToString();
		}
	}
}
