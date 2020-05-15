using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
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

		protected override IObservable<LineOut> CreateConsoleOutObservable()
		{
			if (NoWrapInThread)
				return Observable.Create<LineOut>(observer => KickOff(observer));

			return Observable.Create<LineOut>(async observer =>
			{
				var disposable = await Task.Run(() => KickOff(observer));
				return disposable;
			});
		}

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
			processExited.Subscribe(args => { OnExit(observer); }, e => OnError(observer, e), ()=> OnCompleted(observer));
	}
}
