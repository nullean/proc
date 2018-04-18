using System.Collections.Generic;
using System.Text;
using ProcNet.Std;

namespace ProcNet
{
	public class ProcessResult
	{
		public bool Completed { get; }
		public int? ExitCode { get; }
		public IList<LineOut> ConsoleOut { get; }

		public ProcessResult(bool completed, IList<LineOut> consoleOut, int? exitCode)
		{
			this.Completed = completed;
			this.ConsoleOut = consoleOut ?? new List<LineOut>();
			this.ExitCode = exitCode;
		}

		public override string ToString()
		{
			var sb = new StringBuilder();
			sb.AppendLine($"Process completed: {Completed}, ExitCode: {ExitCode}");
			sb.AppendLine(new string('-', 8));
			foreach (var o in ConsoleOut)
			{
				sb.AppendLine($"[std{(o.Error ? "err" : "out")}]: {o.Line}");
			}
			return sb.ToString();
		}
	}
}
