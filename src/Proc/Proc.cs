using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;
using ProcNet.Std;

namespace ProcNet
{
	public static class Proc
	{
		private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

		public static ProcessResult Start(string bin, params string[] arguments) => Start(bin, DefaultTimeout, arguments);

		public static ProcessResult Start(string bin, TimeSpan timeout, params string[] arguments) => Start(new StartArguments(bin, arguments), timeout);

		public static ProcessResult Start(string bin, TimeSpan timeout, StartedHandler started, params string[] arguments) => Start(bin, timeout, null, started, arguments);

		public static ProcessResult Start(string bin, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, params string[] arguments) =>
			Start(bin, timeout, consoleOutWriter, null, arguments);

		public static ProcessResult Start(string bin, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, StartedHandler started, params string[] arguments) =>
			Start(new StartArguments(bin, arguments), timeout, consoleOutWriter, started);

		public static ProcessResult Start(StartArguments arguments) => Start(arguments, DefaultTimeout);

		public static ProcessResult Start(StartArguments arguments, TimeSpan timeout) => Start(arguments, timeout, null);

		public static ProcessResult Start(StartArguments arguments, TimeSpan timeout, IConsoleOutWriter consoleOutWriter) =>
			Start(arguments, timeout, consoleOutWriter, null);

		public static ProcessResult Start(StartArguments arguments, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, StartedHandler started)
		{
			using (var composite = new CompositeDisposable())
			{
				var process = new ObservableProcess(arguments);
				if (started != null) process.ProcessStarted += started;
				Exception seenException = null;
				var consoleOut = new List<LineOut>();
				composite.Add(process);
				if (consoleOutWriter == null) composite.Add(process.SubscribeLines(consoleOut.Add, e => seenException = e));
				else
				{
					var observable = process.Publish();
					composite.Add(observable.Select(LineOut.From).Subscribe(consoleOut.Add, e => seenException = e));
					composite.Add(observable
						.Subscribe(consoleOutWriter.Write, consoleOutWriter.Write, delegate { })
					);
					composite.Add(observable.Connect());
				}

				var completed = process.WaitForCompletion(timeout);
				if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
				return new ProcessResult(completed, consoleOut, process.ExitCode);
			}
		}
	}

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
