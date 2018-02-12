using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Proc.Extensions;
using Proc.Std;

namespace Proc
{
	/// <summary>
#pragma warning disable 1574
	/// This reads <see cref="Process.StandardOutput"/> and <see cref="Process.StandardError"/> using <see cref="StreamReader.ReadAsync"/>.
	///
	/// When the process exits it waits for these stream readers to finish up to whatever <see cref="WaitForStreamReadersTimeout"/>
	/// is configured to.
	///
	/// This catches all cases where <see cref="EventBasedObservableProcess"/> would fall short to capture all the process output.
	///
	/// <see cref="CharactersOut"/> contains the current char[] buffer which could be fed to <see cref="Console.Write"/> directly.
	///
	/// Note that there is a subclass <use cref="ObservableProcess"/> that allows you to subscribe console output per line
	/// instead whilst still reading the actual process output using asynchronous stream readers.
#pragma warning restore 1574
	/// </summary>
	public class BufferedObservableProcess : ObservableProcessBase<CharactersOut>
	{
		/// <summary>
		/// How long we should wait for the output stream readers to finish when the process exits before we call
		/// <see cref="ObservableProcessBase{TConsoleOut}.OnCompleted"/> is called. By default waits for 5 seconds.
		/// </summary>
		public TimeSpan WaitForStreamReadersTimeout { get; set; } = TimeSpan.FromSeconds(5);

		/// <summary>
		/// Expert level setting: the maximum number of characters to read per itteration. Defaults to 256
		/// </summary>
		public int BufferSize { get; set; } = 256;

		public BufferedObservableProcess(string binary, params string[] arguments) : base(binary, arguments) { }

		public BufferedObservableProcess(StartArguments startArguments) : base(startArguments) { }

		protected override IObservable<CharactersOut> CreateConsoleOutObservable()
		{
			if (this.NoWrapInThread)
				return Observable.Create<CharactersOut>(observer =>
				{
					KickOff(observer);
					return Disposable.Empty;
				});

			return Observable.Create<CharactersOut>(async observer =>
			{
				await Task.Run(() => KickOff(observer));
				return Disposable.Empty;
			});
		}

		private void KickOff(IObserver<CharactersOut> observer)
		{
			if (!this.StartProcess(observer)) return;

			this.Started = true;

			if (this.Process.HasExited)
			{
				OnExit(observer);
				return;
			}

			var stdOutSubscription = this.Process.ObserveStandardOutBuffered(observer, BufferSize);
			var stdErrSubscription = this.Process.ObserveErrorOutBuffered(observer, BufferSize);

			this.Process.Exited += (o, s) =>
			{
				if (!Task.WaitAll(new[] {stdOutSubscription, stdErrSubscription}, WaitForStreamReadersTimeout))
					OnError(observer, new ObservableProcessException(
						$"Waited {WaitForStreamReadersTimeout} unsuccesfully for stdout/err subscriptions to complete after the the process exited"
					));

				OnExit(observer);
			};

			Task.WhenAll(stdOutSubscription, stdErrSubscription)
				.ContinueWith((t) => { OnExit(observer); });
		}
	}
}
