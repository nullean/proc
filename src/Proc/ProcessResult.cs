namespace ProcNet
{
	public class ProcessResult
	{
		public bool Completed { get; }
		public int? ExitCode { get; }

		public ProcessResult(bool completed, int? exitCode)
		{
			Completed = completed;
			ExitCode = exitCode;
		}

		public override string ToString() => $"Process completed: {Completed}, ExitCode: {ExitCode}";
	}
}
