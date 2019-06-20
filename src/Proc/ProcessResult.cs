namespace ProcNet
{
	public class ProcessResult
	{
		public bool Completed { get; }
		public int? ExitCode { get; }

		public ProcessResult(bool completed, int? exitCode)
		{
			this.Completed = completed;
			this.ExitCode = exitCode;
		}

		public override string ToString() => $"Process completed: {Completed}, ExitCode: {ExitCode}";
	}
}
