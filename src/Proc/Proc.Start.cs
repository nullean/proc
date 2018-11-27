using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;
using ProcNet.Std;

namespace ProcNet
{
	public static partial class Proc
	{
		private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(string bin, params string[] arguments) => Start(bin, DefaultTimeout, arguments);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(string bin, TimeSpan timeout, params string[] arguments) =>
			Start(bin, timeout, started: null, arguments: arguments);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="started">A callback when the process is ready to receive standard in writes</param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(string bin, TimeSpan timeout, StartedHandler started, params string[] arguments) =>
			Start(bin, timeout, ConsoleOutColorWriter.Default, started, arguments);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(string bin, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, params string[] arguments) =>
			Start(bin, timeout, consoleOutWriter, started: null, arguments: arguments);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="started">A callback when the process is ready to receive standard in writes</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(string bin, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, StartedHandler started, params string[] arguments) =>
			Start(new StartArguments(bin, arguments), timeout, consoleOutWriter, started);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(StartArguments arguments) => Start(arguments, DefaultTimeout);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(StartArguments arguments, TimeSpan timeout) =>
			Start(arguments, timeout, ConsoleOutColorWriter.Default);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessResult Start(StartArguments arguments, TimeSpan timeout, IConsoleOutWriter consoleOutWriter) =>
			Start(arguments, timeout, consoleOutWriter, null);

		/// <summary> Starts a programs and captures the output while writing to the console at the same time </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="started">A callback when the process is ready to receive standard in writes</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
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
					 composite.Add(process.SubscribeLinesAndCharacters(
						 consoleOut.Add,
						 e => seenException = e,
						 consoleOutWriter.Write,
						 consoleOutWriter.Write
					 )
					);
				}

				var completed = process.WaitForCompletion(timeout);
				if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
				return new ProcessResult(completed, consoleOut, process.ExitCode);
			}
		}
	}
}
