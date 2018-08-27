using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Extensions;
using ProcNet.Std;

namespace ProcNet
{
	/// <summary>
	/// This reads <see cref="Process.StandardOutput"/> and <see cref="Process.StandardError"/> using <see cref="StreamReader.ReadAsync"/>.
	///
	/// When the process exits it waits for these stream readers to finish up to whatever <see cref="WaitForStreamReadersTimeout"/>
	/// is configured to.
	///
	/// This catches all cases where <see cref="EventBasedObservableProcess"/> would fall short to capture all the process output.
	///
#pragma warning disable 1574
	/// <see cref="CharactersOut"/> contains the current char[] buffer which could be fed to <see cref="Console.Write"/> directly.
#pragma warning restore 1574
	///
	/// Note that there is a subclass <use cref="ObservableProcess"/> that allows you to subscribe console output per line
	/// instead whilst still reading the actual process output using asynchronous stream readers.
	/// </summary>
	public class BufferedObservableProcess : ObservableProcessBase<CharactersOut>
	{
		public BufferedObservableProcess(string binary, params string[] arguments) : base(binary, arguments) { }

		public BufferedObservableProcess(StartArguments startArguments) : base(startArguments) { }

		/// <summary>
		/// How long we should wait for the output stream readers to finish when the process exits before we call
		/// <see cref="ObservableProcessBase{TConsoleOut}.OnCompleted"/> is called. By default waits for 10 seconds.
		/// </summary>
		private TimeSpan WaitForStreamReadersTimeout => this.StartArguments.WaitForStreamReadersTimeout;

		/// <summary>
		/// Expert level setting: the maximum number of characters to read per itteration. Defaults to 256
		/// </summary>
		public int BufferSize { get; set; } = 256;

		private CancellationTokenSource _ctx = new CancellationTokenSource();
		private Task _stdOutSubscription;
		private Task _stdErrSubscription;
		private IObserver<CharactersOut> _observer;

		protected override IObservable<CharactersOut> CreateConsoleOutObservable()
		{
			if (this.NoWrapInThread)
				return Observable.Create<CharactersOut>(observer =>
				{
					var disposable = KickOff(observer);
					return disposable;
				});

			return Observable.Create<CharactersOut>(async observer =>
			{
				var disposable = await Task.Run(() => KickOff(observer));
				return disposable;
			});
		}

		/// <summary>
		/// Expert setting, subclasses can return true if a certain condition is met to break out of the async readers on StandardOut and StandardError
		/// </summary>
		/// <returns></returns>
		protected virtual bool ContinueReadingFromProcessReaders() => true;

		private IDisposable KickOff(IObserver<CharactersOut> observer)
		{
			if (!this.StartProcess(observer)) return Disposable.Empty;

			this.Started = true;

			if (this.Process.HasExited)
			{
				OnExit(observer);
				return Disposable.Empty;
			}

			this._observer = observer;
			this.StartAsyncReads();

			this.Process.Exited += (o, s) =>
			{
				WaitForEndOfStreams(observer, _stdOutSubscription, _stdErrSubscription);
				OnExit(observer);
			};

			var disposable = Disposable.Create(() =>
			{
			});

			return disposable;
		}

		private readonly object _lock = new object();
		private bool _reading = false;

		/// <summary>
		/// Allows you to stop reading the console output after subscribing on the observable while leaving the underlying
		/// process running.
		/// </summary>
		public void CancelAsyncReads()
		{
			if (!this._reading) return;
			lock (_lock)
			{
				if (!this._reading) return;
				try
				{
					this._ctx.Cancel();
					this.Process.StandardOutput.BaseStream.Flush();
					this.Process.StandardError.BaseStream.Flush();
				}
				finally
				{
					this._ctx = new CancellationTokenSource();
					this._reading = false;
				}
			}
		}

		public bool IsActivelyReading => IsNotCompletedTask(this._stdOutSubscription) && IsNotCompletedTask(this._stdErrSubscription);

		private static bool IsNotCompletedTask(Task t) => t.Status != TaskStatus.Canceled && t.Status != TaskStatus.RanToCompletion && t.Status != TaskStatus.Faulted;

		/// <summary>
		/// Start reading the console output again after calling <see cref="CancelAsyncReads"/>
		/// </summary>
		public void StartAsyncReads()
		{
			if (this._reading) return;
			lock (_lock)
			{
				if (this._reading) return;

				this.Process.StandardOutput.BaseStream.Flush();
				this.Process.StandardError.BaseStream.Flush();

				this._stdOutSubscription = this.Process.ObserveStandardOutBuffered(_observer, BufferSize, ContinueReadingFromProcessReaders, _ctx.Token);
				this._stdErrSubscription = this.Process.ObserveErrorOutBuffered(_observer, BufferSize, ContinueReadingFromProcessReaders, _ctx.Token);
				this._reading = true;
			}
		}

		protected virtual void OnBeforeWaitForEndOfStreamsError(TimeSpan waited) { }

		private void WaitForEndOfStreams(IObserver<CharactersOut> observer, Task stdOutSubscription, Task stdErrSubscription)
		{
			if (!this._reading) return;
			this.SendYesForBatPrompt();
			if (!Task.WaitAll(new[] {stdOutSubscription, stdErrSubscription}, WaitForStreamReadersTimeout))
			{
				this.CancelAsyncReads();
				this.OnBeforeWaitForEndOfStreamsError(WaitForStreamReadersTimeout);
				OnError(observer, new WaitForEndOfStreamsTimeoutException(WaitForStreamReadersTimeout));
			}
		}
	}
}
