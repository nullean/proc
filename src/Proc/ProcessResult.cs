using System.Collections.Generic;
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
			this.ConsoleOut = consoleOut;
			this.ExitCode = exitCode;
		}
	}
}