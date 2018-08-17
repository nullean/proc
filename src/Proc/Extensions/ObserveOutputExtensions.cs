using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Std;
using static System.Threading.Tasks.TaskCreationOptions;

namespace ProcNet.Extensions
{
	internal static class ObserveOutputExtensions
	{
		public static IObservable<LineOut> ObserveErrorOutLineByLine(this Process process)
		{
			var receivedStdErr =
				Observable.FromEventPattern<DataReceivedEventHandler, DataReceivedEventArgs>
					(h => process.ErrorDataReceived += h, h => process.ErrorDataReceived -= h)
					.Where(e => e.EventArgs.Data != null)
					.Select(e => ConsoleOut.ErrorOut(e.EventArgs.Data));

			return Observable.Create<LineOut>(observer =>
			{
				var cancel = Disposable.Create(()=>{ try { process.CancelErrorRead(); } catch(InvalidOperationException) { } });
				return new CompositeDisposable(cancel, receivedStdErr.Subscribe(observer));
			});
		}

		public static IObservable<LineOut> ObserveStandardOutLineByLine(this Process process)
		{
			var receivedStdOut =
				Observable.FromEventPattern<DataReceivedEventHandler, DataReceivedEventArgs>
					(h => process.OutputDataReceived += h, h => process.OutputDataReceived -= h)
					.Where(e => e.EventArgs.Data != null)
					.Select(e => ConsoleOut.Out(e.EventArgs.Data));

			return Observable.Create<LineOut>(observer =>
			{
				var cancel = Disposable.Create(()=>{ try { process.CancelOutputRead(); } catch(InvalidOperationException) { } });
				return new CompositeDisposable(cancel, receivedStdOut.Subscribe(observer));
			});
		}

		public static Task ObserveErrorOutBuffered(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering, CancellationToken token) =>
			RunBufferedRead(process, observer, bufferSize, keepBuffering, ConsoleOut.ErrorOut, process.StandardError, token);

		public static Task ObserveStandardOutBuffered(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering, CancellationToken token) =>
			RunBufferedRead(process, observer, bufferSize, keepBuffering, ConsoleOut.Out, process.StandardOutput, token);

		private static Task RunBufferedRead(Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering, Func<char[], CharactersOut> m,
			StreamReader reader, CancellationToken token) =>
			Task.Factory.StartNew(() => BufferedRead(process, reader, observer, bufferSize, m, keepBuffering, token), token, LongRunning, TaskScheduler.Current);


		private static async Task BufferedRead(Process p, StreamReader r, IObserver<CharactersOut> o, int b, Func<char[], CharactersOut> m, Func<bool> keepBuffering,
			CancellationToken token)
		{
			using(token.Register(() =>
			{
				// this breaks out of the ReadAsync below causing it to throw a cancellation exception
				// on the next line
				r.DiscardBufferedData();
			}, useSynchronizationContext: false))
			while (keepBuffering() && !r.EndOfStream)
			{
				var buffer = new char[b];
				var read = await r.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
				token.ThrowIfCancellationRequested();
				if (read > 0)
					o.OnNext(m(buffer));
				else
					await Task.Delay(10).ConfigureAwait(false);
			}
		}

		//attempt at a routine that uses a cancellable ReadAsync..
		private static async Task BufferedRead2(Process process, StreamReader r, IObserver<CharactersOut> o, int b, Func<char[], CharactersOut> m, Func<bool> keepBuffering)
		{
			var readZeroAfterExited = 0;
			while (keepBuffering())
			{
				if (readZeroAfterExited > 10) break;

				var ctx  = new CancellationTokenSource(TimeSpan.FromSeconds(1));
				var buffer = new byte[b];
				var read = await r.BaseStream.ReadAsync(buffer, 0, buffer.Length, ctx.Token).ConfigureAwait(false);
				if (read > 0)
				{
					var charCount = Encoding.UTF8.GetCharCount(buffer);
					if (charCount != 256)
					{

					}
					var chars = Encoding.UTF8.GetChars(buffer);

					o.OnNext(m(chars));
				}
				else if (read == 0 && (readZeroAfterExited > 0 || process.HasExited))
				{
					readZeroAfterExited++;
				}
				else
					await Task.Delay(10).ConfigureAwait(false);
			}
		}



	}
}
