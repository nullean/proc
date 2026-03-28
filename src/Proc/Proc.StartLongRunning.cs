using System;
using System.Reactive.Disposables;
using System.Runtime.ExceptionServices;
using System.Threading;
using ProcNet.Extensions;
using ProcNet.Std;

namespace ProcNet
{
	public class LongRunningApplicationSubscription : IDisposable
	{
		internal LongRunningApplicationSubscription(ObservableProcess process, CompositeDisposable subscription)
		{
			Process = process;
			Subscription = subscription;
		}

		private IDisposable Subscription { get; }

		private ObservableProcess Process { get; }

		public bool Running { get; internal set; }

		public ManualResetEvent WaitHandle { get; } = new(false);

		/// <inheritdoc cref="ObservableProcessBase{TConsoleOut}.SendControlC(int)"/>>
		public bool SendControlC(int processId) => Process.SendControlC(processId);

		/// <inheritdoc cref="ObservableProcessBase{TConsoleOut}.SendControlC()"/>>
		public void SendControlC() => Process.SendControlC();

		public void Dispose()
		{
			Subscription?.Dispose();
			Process?.Dispose();
			WaitHandle?.Dispose();
		}
	}

	public static partial class Proc
	{

		/// <summary>
		/// Allows you to start long running processes and dispose them when needed.
		/// <para> It also optionally allows you to wait <paramref name="waitForStartedConfirmation"/> before returning the disposable
		/// by inspecting the console out of the process to validate the 'true' starting confirmation of the process using
		/// <see cref="LongRunningArguments.StartedConfirmationHandler"/>
		/// </para>
		/// <para>
		/// A usecase for this could be starting a webserver or database and wait before it prints it startup confirmation
		/// before returning.
		/// </para>
		/// </summary>
		/// <param name="arguments">Encompasses all the options you can specify to start Proc processes</param>
		/// <param name="waitForStartedConfirmation">Waits returning the <see cref="IDisposable"/> before <see cref="LongRunningArguments.StartedConfirmationHandler"/> confirms the process started</param>
		/// <param name="consoleOutWriter">
		/// An implementation of <see cref="IConsoleOutWriter"/> that takes care of writing to the console
		/// <para>defaults to <see cref="ConsoleOutColorWriter"/> which writes standard error messages in red</para>
		/// </param>
		/// <param name="ct">Cancels the startup confirmation wait and disposes the subscription when signalled</param>
		/// <returns>The exit code and whether the process completed</returns>
		public static LongRunningApplicationSubscription StartLongRunning(LongRunningArguments arguments, TimeSpan waitForStartedConfirmation, IConsoleOutWriter consoleOutWriter = null, CancellationToken ct = default)
		{
			var composite = new CompositeDisposable();
			var process = new ObservableProcess(arguments);
			var subscription = new LongRunningApplicationSubscription(process, composite);
			consoleOutWriter ??= new ConsoleOutColorWriter();

			var startedConfirmation = arguments.StartedConfirmationHandler ?? (_ => true);

			if (arguments.StartedConfirmationHandler != null && arguments.StopBufferingAfterStarted)
				arguments.KeepBufferingLines = _ => !subscription.Running;

			Exception seenException = null;
			composite.Add(process);
			composite.Add(process.SubscribeLinesAndCharacters(
					l =>
					{
						if (!startedConfirmation(l)) return;
						subscription.Running = true;
						subscription.WaitHandle.Set();
					},
					e =>
					{
						seenException = e;
						subscription.Running = false;
						subscription.WaitHandle.Set();
					},
					l => consoleOutWriter.Write(l),
					l => consoleOutWriter.Write(l),
					onCompleted: () =>
					{
						subscription.Running = false;
						subscription.WaitHandle.Set();
					})
			);

			if (seenException != null) ExceptionDispatchInfo.Capture(seenException).Throw();
			if (arguments.StartedConfirmationHandler == null)
			{
				subscription.Running = true;
				subscription.WaitHandle.Set();
			}
			else
			{
				bool confirmed;
				if (ct.CanBeCanceled)
				{
					var handles = new WaitHandle[] { subscription.WaitHandle, ct.WaitHandle };
					var index = WaitHandle.WaitAny(handles, waitForStartedConfirmation);
					if (index == 1) // cancellation token fired
					{
						subscription.Dispose();
						ct.ThrowIfCancellationRequested();
					}
					confirmed = index == 0;
				}
				else
					confirmed = subscription.WaitHandle.WaitOne(waitForStartedConfirmation);

				if (confirmed) return subscription;
				var pwd = arguments.WorkingDirectory;
				var args = arguments.Args;
				var printBinary = arguments.OnlyPrintBinaryInExceptionMessage
					? $"\"{arguments.Binary}\""
					: $"\"{arguments.Binary} {args.NaivelyQuoteArguments()}\"{(pwd == null ? string.Empty : $" pwd: {pwd}")}";
				throw new ProcExecException($"Could not yield started confirmation after {waitForStartedConfirmation} while running {printBinary}");
			}

			return subscription;
		}
	}
}
