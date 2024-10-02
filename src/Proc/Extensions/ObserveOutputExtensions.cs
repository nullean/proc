using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Std;

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
			BufferedRead(process, process.StandardError, observer, bufferSize, ConsoleOut.ErrorOut, keepBuffering, token);

		public static Task ObserveStandardOutBuffered(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering, CancellationToken token) =>
			BufferedRead(process, process.StandardOutput, observer, bufferSize, ConsoleOut.Out, keepBuffering, token);

		private static async Task BufferedRead(Process p, StreamReader r, IObserver<CharactersOut> o, int b, Func<char[], CharactersOut> m, Func<bool> keepBuffering, CancellationToken token)
		{
			using (var sr = new CancellableStreamReader(r.BaseStream, Encoding.UTF8, true, b, true, token))
				while (keepBuffering())
				{
					var buffer = new char[b];
					var read = await sr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
					if (token.IsCancellationRequested) break;

					token.ThrowIfCancellationRequested();
					if (read > 0)
						o.OnNext(m(buffer));
					else
					{
						if (await sr.EndOfStreamAsync()) break;
						await Task.Delay(1, token).ConfigureAwait(false);
					}
				}

			token.ThrowIfCancellationRequested();
		}

		public static void ReadStandardErrBlocking(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering) =>
			BufferedReadBlocking(process, process.StandardError, observer, bufferSize, ConsoleOut.ErrorOut, keepBuffering);

		public static void ReadStandardOutBlocking(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering) =>
			BufferedReadBlocking(process, process.StandardOutput, observer, bufferSize, ConsoleOut.Out, keepBuffering);

		private static void BufferedReadBlocking(this Process p, StreamReader r, IObserver<CharactersOut> o, int b, Func<char[], CharactersOut> m, Func<bool> keepBuffering)
		{
			using var sr = new StreamReader(r.BaseStream, Encoding.UTF8, true, b, true);
			while (keepBuffering())
			{
				var buffer = new char[b];
				var read = sr.Read(buffer, 0, buffer.Length);

				if (read > 0)
					o.OnNext(m(buffer));
				else
				if (sr.EndOfStream) break;
			}
		}

	}
}
