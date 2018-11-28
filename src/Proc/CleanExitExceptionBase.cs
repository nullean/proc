using System;

namespace ProcNet
{

	public class WaitForEndOfStreamsTimeoutException : ObservableProcessException
	{
		public WaitForEndOfStreamsTimeoutException(TimeSpan wait)
			: base($"Waited {wait} unsuccesfully for stdout/err subscriptions to complete after the the process exited") { }
	}

	public class ObservableProcessException : CleanExitExceptionBase
	{
		public ObservableProcessException(string message) : base(message) { }
		public ObservableProcessException(string message, string helpText, Exception innerException) : base(message, helpText, innerException) { }
	}

	public class CleanExitExceptionBase : Exception
	{
		public CleanExitExceptionBase(string message) : base(message) { }
		public CleanExitExceptionBase(string message, string helpText, Exception innerException)
			: base(message, innerException) => HelpText = helpText;

		public string HelpText { get; private set; }
	}

	public class ProcExecException : CleanExitExceptionBase
	{
		public ProcExecException(string message) : base(message) { }

		public ProcExecException(string message, string helpText, Exception innerException)
			: base(message, helpText, innerException) { }

		public int? ExitCode { get; set; }
	}
}
