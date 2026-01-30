using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ProcNet.Extensions;
using ProcNet.Std;

namespace ProcNet
{
	/// <summary>
	/// This implementation wraps over <see cref="Process.OutputDataReceived"/> and <see cref="Process.ErrorDataReceived"/>
	/// it utilizes a double call to <see cref="Process.WaitForExit()"/> once with timeout and once without to ensure all events are
	/// received.
	/// </summary>
	public class EventBasedObservableProcess: ObservableProcessBase<LineOut>, ISubscribeLines
	{
		public EventBasedObservableProcess(string binary, params string[] arguments) : base(binary, arguments) { }

		public EventBasedObservableProcess(StartArguments startArguments) : base(startArguments) { }

		protected override IObservable<LineOut> CreateConsoleOutObservable() =>
			Observable.Create<LineOut>(observer => KickOff(observer));

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
	}
}
