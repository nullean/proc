using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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

		public static Task ObserveErrorOutBuffered(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool> keepBuffering)
		{
			var reader = process.StandardError;
			return Task.Run(async () => await BufferedRead(reader, observer, bufferSize, ConsoleOut.ErrorOut, keepBuffering));
		}

		public static Task ObserveStandardOutBuffered(this Process process, IObserver<CharactersOut> observer, int bufferSize, Func<bool>  keepBuffering)
		{
			var reader = process.StandardOutput;
			return Task.Run(async () => await BufferedRead(reader, observer, bufferSize, ConsoleOut.Out, keepBuffering));
		}

		private static async Task BufferedRead(StreamReader r, IObserver<CharactersOut> o, int b, Func<char[], CharactersOut> m, Func<bool> keepBuffering)
		{
			while (!r.EndOfStream && keepBuffering())
			{
				var buffer = new char[b];
				var read = await r.ReadAsync(buffer, 0, buffer.Length);
				if (read > 0)
					o.OnNext(m(buffer));
				else
					await Task.Delay(10);
			}
		}


	}
}
