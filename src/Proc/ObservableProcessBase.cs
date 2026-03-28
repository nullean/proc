using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using ProcNet.Extensions;
using ProcNet.Std;

namespace ProcNet
{
	public delegate void StandardInputHandler(StreamWriter standardInput);

	public abstract class ObservableProcessBase<TConsoleOut> : IObservableProcess<TConsoleOut>
		where TConsoleOut : ConsoleOut
	{
		protected ObservableProcessBase(string binary, params string[] arguments)
			: this(new StartArguments(binary, arguments)) { }

		protected ObservableProcessBase(StartArguments startArguments)
		{
			StartArguments = startArguments ?? throw new ArgumentNullException(nameof(startArguments));
			Process = CreateProcess();
			if (startArguments.StandardInputHandler != null)
				StandardInputReady += startArguments.StandardInputHandler;
			CreateObservable();
		}

		public virtual IDisposable Subscribe(IObserver<TConsoleOut> observer) => OutStream.Subscribe(observer);

		public IDisposable Subscribe(IConsoleOutWriter writer) => OutStream.Subscribe(writer.Write, writer.Write, delegate { });

		private readonly ManualResetEvent _completedHandle = new(false);

		public StreamWriter StandardInput => Process.StandardInput;
		public string Binary => StartArguments.Binary;
		public int? ExitCode { get; private set; }

		protected StartArguments StartArguments { get; }
		protected Process Process { get; }
		protected bool Started { get; set; }
		protected string ProcessName { get; private set; }

		[Obsolete("Task.Run wrapping has been removed. This property has no effect.")]
		protected bool NoWrapInThread => StartArguments.NoWrapInThread;
		private int? _processId;
		public virtual int? ProcessId => _processId;

		protected IObservable<TConsoleOut> OutStream { get; private set; } = Observable.Empty<TConsoleOut>();

		private void CreateObservable()
		{
			if (Started) return;
			_completedHandle.Reset();
			OutStream = CreateConsoleOutObservable();
		}

		protected abstract IObservable<TConsoleOut> CreateConsoleOutObservable();

		public event StandardInputHandler StandardInputReady = (s) => { };

		protected bool StartProcess(IObserver<TConsoleOut> observer)
		{
			var started = false;
			try
			{
				started = Process.Start();
				if (started)
				{
					try
					{
						_processId = Process.Id;
						ProcessName = Process.ProcessName;
					}
					catch (InvalidOperationException)
					{
						// best effort, Process could have finished before even attempting to read .Id and .ProcessName
						// which can throw if the process exits in between
					}
					StandardInputReady(Process.StandardInput);
					return true;
				}

				OnError(observer, new ObservableProcessException($"Failed to start observable process: {Binary}"));
				return false;
			}
			catch (Exception e)
			{
				OnError(observer, new ObservableProcessException($"Exception while starting observable process: {Binary}", e.Message, e));
			}
			finally
			{
				if (!started) SetCompletedHandle();
			}

			return false;
		}

		protected virtual void OnError(IObserver<TConsoleOut> observer, Exception e)
		{
			HardKill();
			observer.OnError(e);
		}

		protected virtual void OnCompleted(IObserver<TConsoleOut> observer) => observer.OnCompleted();

		private readonly object _exitLock = new object();

		protected void OnExit(IObserver<TConsoleOut> observer)
		{
			if (!Started) return;
			int? exitCode = null;
			try
			{
				exitCode = Process.ExitCode;
			}
			//ExitCode and HasExited are all trigger happy. We are aware the process may or may not have an exit code.
			catch (InvalidOperationException) { }
			finally
			{
				ExitStop(observer, exitCode);
			}
		}

		private void ExitStop(IObserver<TConsoleOut> observer, int? exitCode)
		{
			if (!Started) return;
			if (_disposing) return;
			lock (_exitLock)
			{
				if (!Started) return;

				Stop(exitCode, observer);
			}
		}

		private Process CreateProcess()
		{
			var s = StartArguments;
			var args = s.Args;
			var processStartInfo = new ProcessStartInfo
			{
				FileName = s.Binary,
				#if STRING_ARGS
				Arguments = args != null ? string.Join(" ", args) : string.Empty,
				#endif
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true
			};
			#if !STRING_ARGS
			foreach (var arg in s.Args)
				processStartInfo.ArgumentList.Add(arg);
			#endif
			if (s.Environment != null)
			{
				foreach (var kv in s.Environment)
				{
					processStartInfo.Environment[kv.Key] = kv.Value;
				}
			}

			if (!string.IsNullOrWhiteSpace(s.WorkingDirectory)) processStartInfo.WorkingDirectory = s.WorkingDirectory;

			var p = new Process
			{
				EnableRaisingEvents = true,
				StartInfo = processStartInfo
			};
			return p;
		}

		/// <summary>
		/// Block until the process completes.
		/// </summary>
		/// <param name="timeout">The maximum time span we are willing to wait</param>
		/// <param name="ct">Cancels the wait and stops the process when signalled</param>
		/// <exception cref="CleanExitExceptionBase">an exception that indicates a problem early in the pipeline</exception>
		/// <exception cref="OperationCanceledException">when <paramref name="ct"/> is cancelled</exception>
		public bool WaitForCompletion(TimeSpan? timeout, CancellationToken ct = default)
		{
			if (!ct.CanBeCanceled)
			{
				if (_completedHandle.WaitOne(timeout ?? TimeSpan.FromMilliseconds(-1))) return true;
				Stop();
				return false;
			}

			var handles = new WaitHandle[] { _completedHandle, ct.WaitHandle };
			var index = WaitHandle.WaitAny(handles, timeout ?? TimeSpan.FromMilliseconds(-1));

			if (index == WaitHandle.WaitTimeout)
			{
				Stop();
				return false;
			}
			if (index == 1) // cancellation token fired
			{
				Stop();
				ct.ThrowIfCancellationRequested();
			}
			return true;
		}

		private readonly object _unpackLock = new();
		private readonly object _sendLock = new();
		private int _sentControlC; // 0 = not sent, 1 = sent; written via Interlocked


		public bool SendControlC(int processId)
		{
			var platform = (int)Environment.OSVersion.Platform;
			var isWindows = platform != 4 && platform != 6 && platform != 128;
			if (isWindows)
			{
				var path = Path.Combine(Path.GetTempPath(), "proc-c.exe");
				UnpackTempOutOfProcessSignalSender(path);
				lock (_sendLock)
				{
					var args = new StartArguments(path, processId.ToString(CultureInfo.InvariantCulture))
					{
						WaitForExit = null,
						Timeout = TimeSpan.FromSeconds(5)
					};
					var result = Proc.Start(args);
					SendYesForBatPrompt();
					return result.ExitCode == 0;
				}
			}
			else
			{
				lock (_sendLock)
				{
					// I wish .NET Core had signals baked in but looking at the corefx repos tickets this is not happening any time soon.
					var args = new StartArguments("kill", "-SIGINT", processId.ToString(CultureInfo.InvariantCulture))
					{
						WaitForExit = null,
						Timeout = TimeSpan.FromSeconds(5)
					};
					var result = Proc.Start(args);
					return result.ExitCode == 0;
				}
			}
		}

		public void SendControlC()
		{
			// CompareExchange atomically sets _sentControlC to 1 only if it was 0.
			// Returns the old value — if it was already 1, another thread already sent.
			if (Interlocked.CompareExchange(ref _sentControlC, 1, 0) != 0) return;
			if (!ProcessId.HasValue) return;
			SendControlC(ProcessId.Value);
		}

		protected void SendYesForBatPrompt()
		{
			if (!StopRequested) return;
			if (ProcessName == "cmd")
			{
				try
				{
					StandardInput.WriteLine("Y");
				}
				//best effort
				catch (InvalidOperationException) { }
				catch (IOException) { }
			}
		}

		private void UnpackTempOutOfProcessSignalSender(string path)
		{
			if (File.Exists(path)) return;
			var assembly = typeof(Proc).GetTypeInfo().Assembly;
			try
			{
				lock (_unpackLock)
				{
					if (File.Exists(path)) return;
					using (var stream = assembly.GetManifestResourceStream("ProcNet.Embedded.Proc.ControlC.exe"))
					using (var fs = File.OpenWrite(path))
						stream.CopyTo(fs);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		protected bool StopRequested => _stopRequested || _sentControlC != 0;
		private bool _stopRequested;
		private void Stop(int? exitCode = null, IObserver<TConsoleOut> observer = null)
		{
			try
			{
				_stopRequested = true;
				if (Process == null) return;

				var wait = StartArguments.WaitForExit;
				try
				{
					if (Started && wait.HasValue)
					{
						bool exitted;
						if (StartArguments.SendControlCFirst)
						{
							SendControlC();
							exitted = Process?.WaitForExit((int) wait.Value.TotalMilliseconds) ?? false;
							//still attempt to kill to process if control c failed
							if (!exitted) Process?.Kill();
						}
						else
						{
							Process?.Kill();
							exitted = Process?.WaitForExit((int) wait.Value.TotalMilliseconds) ?? false;
						}

						//if we haven't exited do a hard wait for exit by using the overload that does not timeout.
						if (Process != null && !exitted) HardWaitForExit(TimeSpan.FromSeconds(10));
					}
					else if (Started)
					{
						Process?.Kill();
					}
				}
				//Access denied usually means the program is already terminating.
				catch (Win32Exception) { }
				//This usually indiciates the process is already terminated
				catch (InvalidOperationException) { }
				try
				{
					Process?.Dispose();
				}
				//the underlying call to .Close() can throw an NRE if you dispose too fast after starting
				catch (NullReferenceException) { }
			}
			finally
			{
				if (Started && exitCode.HasValue)
					ExitCode = exitCode.Value;

				Started = false;
				if (observer != null) OnCompleted(observer);
				SetCompletedHandle();
			}
		}

		private void HardKill()
		{
			try
			{
				Process?.Kill();
			}
			catch (Exception)
			{
				// ignored
			}
			finally
			{
				try
				{
					Process?.Dispose();
				}
				catch (Exception)
				{
					// ignored
				}
			}
		}

		protected void SetCompletedHandle()
		{
			OnBeforeSetCompletedHandle();
			_completedHandle.Set();
		}

		protected virtual void OnBeforeSetCompletedHandle() { }

		private bool HardWaitForExit(TimeSpan timeSpan) => Process.HardWaitForExit(timeSpan);

		// volatile: ensures the write is immediately visible to ExitStop on other threads
		// without requiring a lock. Never reset to false — once disposed, always disposed.
		private volatile bool _disposing;

		public void Dispose()
		{
			_disposing = true;       // visible to ExitStop before we enter the lock
			lock (_exitLock)         // prevents concurrent Stop() from ExitStop + Dispose
			{
				Stop();
			}
		}
	}
}
