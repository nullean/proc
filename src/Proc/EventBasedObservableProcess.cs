using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
#if NET11_0_OR_GREATER
using System.Threading;
using System.Threading.Tasks;
#endif
using ProcNet.Extensions;
using ProcNet.Std;

namespace ProcNet
{
#if NET11_0_OR_GREATER
	/// <summary>
	/// This implementation reads standard output and error through <see cref="Process.ReadAllLinesAsync"/>, which
	/// multiplexes both streams on a single thread without blocking any thread pool threads and is deadlock-free
	/// by construction.
	/// </summary>
#else
	/// <summary>
	/// This implementation wraps over <see cref="Process.OutputDataReceived"/> and <see cref="Process.ErrorDataReceived"/>
	/// it utilizes a double call to <see cref="Process.WaitForExit()"/> once with timeout and once without to ensure all events are
	/// received.
	/// </summary>
#endif
	public class EventBasedObservableProcess: ObservableProcessBase<LineOut>, ISubscribeLines
	{
		public EventBasedObservableProcess(string binary, params string[] arguments) : base(binary, arguments) { }

		public EventBasedObservableProcess(StartArguments startArguments) : base(startArguments) { }

		protected override IObservable<LineOut> CreateConsoleOutObservable() =>
			Observable.Create<LineOut>(observer => KickOff(observer));

#if NET11_0_OR_GREATER
		private CompositeDisposable KickOff(IObserver<LineOut> observer)
		{
			if (!StartProcess(observer)) return new CompositeDisposable();

			Started = true;
			var cts = new CancellationTokenSource();
			// Deliberately not awaited/Task.Run'ed: ReadAllLinesAsync performs true async I/O, so running it
			// inline here (rather than on a thread pool thread) is what avoids blocking a thread for the
			// lifetime of the process, which is the whole point of adopting it.
			_ = ReadAllLinesLoop(observer, cts.Token);

			return new CompositeDisposable(Disposable.Create(() => cts.Cancel()));
		}

		private async Task ReadAllLinesLoop(IObserver<LineOut> observer, CancellationToken token)
		{
			try
			{
				await foreach (var line in Process.ReadAllLinesAsync(token).ConfigureAwait(false))
					observer.OnNext(new LineOut(line.StandardError, line.Content));

				try
				{
					await Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
				}
				catch (InvalidOperationException)
				{
					// Process already disposed
				}
				OnExit(observer);
			}
			catch (OperationCanceledException)
			{
				// Subscription disposed while still reading; Stop() already takes care of completing the observer.
			}
			catch (Exception e)
			{
				OnError(observer, e);
			}
		}
#else
		private CompositeDisposable KickOff(IObserver<LineOut> observer)
		{
			var stdOut = Process.ObserveStandardOutLineByLine();
			var stdErr = Process.ObserveErrorOutLineByLine();

			var stdOutSubscription = stdOut.Subscribe(observer);
			var stdErrSubscription = stdErr.Subscribe(observer);

			var processExited = Observable.FromEventPattern(h => Process.Exited += h, h => Process.Exited -= h);
			var processError = CreateProcessExitSubscription(processExited, observer);

			if (!StartProcess(observer))
				return new CompositeDisposable(processError);

			Process.BeginOutputReadLine();
			Process.BeginErrorReadLine();

			Started = true;
			return new CompositeDisposable(stdOutSubscription, stdErrSubscription, processError);
		}

		private IDisposable CreateProcessExitSubscription(IObservable<EventPattern<object>> processExited, IObserver<LineOut> observer) =>
			processExited.Subscribe(args =>
			{
				// Second WaitForExit() call (parameterless) ensures all async events are flushed
				// before we proceed with the exit handling. This is the documented .NET pattern
				// for Process event handling - must be called BEFORE the process is disposed.
				try
				{
					Process?.WaitForExit();
				}
				catch (InvalidOperationException)
				{
					// Process already disposed
				}
				OnExit(observer);
			}, e => OnError(observer, e), ()=> OnCompleted(observer));
#endif
	}
}
