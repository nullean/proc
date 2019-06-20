using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Runtime.ExceptionServices;
using ProcNet.Std;

namespace ProcNet
{
	public static partial class Proc
	{
		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(IConsoleLineHandler lineHandler, string bin, params string[] arguments) => 
			StartRedirected(lineHandler, bin, DefaultTimeout, arguments);

		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(IConsoleLineHandler lineHandler, string bin, TimeSpan timeout, params string[] arguments) =>
			StartRedirected(lineHandler, bin, timeout, started: null, arguments: arguments);

		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="started">A callback when the process is ready to receive standard in writes</param>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(IConsoleLineHandler lineHandler, string bin, TimeSpan timeout, StartedHandler started, params string[] arguments) =>
			StartRedirected(new StartArguments(bin, arguments), timeout, started, lineHandler);

		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(StartArguments arguments, IConsoleLineHandler lineHandler = null) => 
			StartRedirected(arguments, DefaultTimeout, started: null, lineHandler: lineHandler);

		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(StartArguments arguments, TimeSpan timeout, IConsoleLineHandler lineHandler = null) =>
			StartRedirected(arguments, timeout, started: null, lineHandler: lineHandler);

		/// <summary>
		/// Start a program and get notified of lines in realtime through <see cref="lineHandler"/> unlike <see cref="Start(string,string[])"/>
		/// This won't capture all lines on the returned object and won't default to writing to the Console.
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="started">A callback when the process is ready to receive standard in writes</param>
		/// <param name="lineHandler">
		/// An implementation of <see cref="IConsoleLineHandler"/> that receives every line as <see cref="LineOut"/> or the <see cref="Exception"/> that occurs while running
		/// </param>
		/// <returns>The exit code and whether the process completed</returns>
		public static ProcessResult StartRedirected(StartArguments arguments, TimeSpan timeout, StartedHandler started, IConsoleLineHandler lineHandler = null)
		{
			using (var composite = new CompositeDisposable())
			{
				var process = new ObservableProcess(arguments);
				if (started != null) process.ProcessStarted += started;

				Exception seenException = null;
				composite.Add(process);
				composite.Add(process.SubscribeLines(
					l => lineHandler?.Handle(l),
					e =>
					{
						seenException = e;
						lineHandler?.Handle(e);
					})
				);

				var completed = process.WaitForCompletion(timeout);
				if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
				return new ProcessResult(completed, process.ExitCode);
			}
		}
	}
}
