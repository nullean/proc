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
		/// Default timeout for all the process started through Proc.Start() or Proc.Exec().
		/// Defaults to infinity.
		/// </summary>
		public static TimeSpan DefaultTimeout { get; } = new(0, 0, 0, 0, -1);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="bin">The binary to execute</param>
		/// <param name="arguments">the commandline arguments to add to the invocation of <paramref name="bin"/></param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(string bin, params string[] arguments) =>
			Start(bin, DefaultTimeout, arguments);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="bin">The binary to execute</param>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="arguments">the commandline arguments to add to the invocation of <paramref name="bin"/></param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(string bin, TimeSpan timeout, params string[] arguments) =>
			Start(bin, timeout, null, arguments);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="bin">The binary to execute</param>
		/// <param name="arguments">the commandline arguments to add to the invocation of <paramref name="bin"/></param>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(string bin, TimeSpan timeout, IConsoleOutWriter consoleOutWriter, params string[] arguments) =>
			Start(new StartArguments(bin, arguments), timeout, consoleOutWriter);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="arguments">Encompasses all the options you can specify to start Proc processes</param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(StartArguments arguments) => Start(arguments, DefaultTimeout);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="arguments">Encompasses all the options you can specify to start Proc processes</param>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(StartArguments arguments, TimeSpan timeout) =>
			Start(arguments, timeout, null);

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="arguments">Encompasses all the options you can specify to start Proc processes</param>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(StartArguments arguments, TimeSpan timeout, IConsoleOutWriter consoleOutWriter)
		{
			using var composite = new CompositeDisposable();
			var process = new ObservableProcess(arguments);
			consoleOutWriter ??= new ConsoleOutColorWriter();

			Exception seenException = null;
			var consoleOut = new List<LineOut>();
			composite.Add(process);
			composite.Add(process.SubscribeLinesAndCharacters(
					l =>
					{
						consoleOut.Add(l);
					},
					e => seenException = e,
					l => consoleOutWriter.Write(l),
					l => consoleOutWriter.Write(l)
				)
			);

			var completed = process.WaitForCompletion(timeout);
			if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
			return new ProcessCaptureResult(completed, consoleOut, process.ExitCode);
		}
	}
}
