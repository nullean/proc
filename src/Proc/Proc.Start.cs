using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Runtime.ExceptionServices;
using ProcNet.Std;

namespace ProcNet
{
	public static partial class Proc
	{
		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="bin">The binary to execute</param>
		/// <param name="arguments">the commandline arguments to add to the invocation of <paramref name="bin"/></param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(string bin, params string[] arguments) =>
			Start(new StartArguments(bin, arguments));

		/// <summary> Starts a program and captures the output while writing to the console in realtime during execution </summary>
		/// <param name="arguments">Encompasses all the options you can specify to start Proc processes</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <returns>An object holding a list of console out lines, the exit code and whether the process completed</returns>
		public static ProcessCaptureResult Start(StartArguments arguments)
		{
			using var composite = new CompositeDisposable();
			var process = new ObservableProcess(arguments);
			var consoleOutWriter = arguments.ConsoleOutWriter ?? new ConsoleOutColorWriter();

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

			var completed = process.WaitForCompletion(arguments.Timeout);
			if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
			return new ProcessCaptureResult(completed, consoleOut, process.ExitCode);
		}
	}
}
