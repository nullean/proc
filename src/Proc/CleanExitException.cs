using System;

namespace ProcNet
{

	public class WaitForEndOfStreamsTimeoutException : ObservableProcessException
	{
		public WaitForEndOfStreamsTimeoutException(TimeSpan wait)
			: base($"Waited {wait} unsuccesfully for stdout/err subscriptions to complete after the the process exited") { }
	}

	public class ObservableProcessException : CleanExitException
	{
		public ObservableProcessException(string message) : base(message) { }
		public ObservableProcessException(string message, Exception innerException) : base(message, innerException) { }
		public ObservableProcessException(string message, string helpText) : base(message, helpText) { }
		public ObservableProcessException(string message, string helpText, Exception innerException) : base(message, helpText, innerException) { }
	}

	public class CleanExitException : Exception
	{
		public CleanExitException(string message) : base(message) { }
		public CleanExitException(string message, Exception innerException) : base(message, innerException) { }

		public CleanExitException(string message, string helpText) : base(message)
		{
			this.HelpText = helpText;
		}
		public CleanExitException(string message, string helpText, Exception innerException) : base(message, innerException)
		{
			this.HelpText = helpText;
		}

		public string HelpText { get; private set; }
	}
}
