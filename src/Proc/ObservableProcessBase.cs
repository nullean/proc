using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Std;

namespace ProcNet
{
	public delegate void StartedHandler(StreamWriter standardInput);

	public abstract class ObservableProcessBase<TConsoleOut> : IObservableProcess<TConsoleOut>
		where TConsoleOut : ConsoleOut
	{
		private readonly StartArguments _startArguments;
		private readonly ManualResetEvent _completedHandle = new ManualResetEvent(false);
		protected bool NoWrapInThread { get; }

		protected ObservableProcessBase(string binary, params string[] arguments)
			: this(new StartArguments(binary, arguments)) { }

		public StreamWriter StandardInput => this.Process.StandardInput;

		protected ObservableProcessBase(StartArguments startArguments)
		{
			this._startArguments = startArguments;
			this.NoWrapInThread = startArguments.NoWrapInThread;
			this.Binary = this._startArguments.Binary;
			this.Process = CreateProcess();
			this.CreateObservable();
		}

		public virtual IDisposable Subscribe(IObserver<TConsoleOut> observer) => this.OutStream.Subscribe(observer);

		public IDisposable Subscribe(IConsoleOutWriter writer) => this.OutStream.Subscribe(writer.Write, writer.Write, delegate { });

		protected Process Process { get; }
		protected bool Started { get; set; }

		public string Binary { get; }

		public int? ExitCode { get; private set; }

		public virtual int? ProcessId => this.Process?.Id;

		protected IObservable<TConsoleOut> OutStream { get; private set; } = Observable.Empty<TConsoleOut>();

		private void CreateObservable()
		{
			if (this.Started) return;
			this._completedHandle.Reset();
			this.OutStream = CreateConsoleOutObservable();
		}

		protected abstract IObservable<TConsoleOut> CreateConsoleOutObservable();

		public event StartedHandler ProcessStarted = (s) => { };

		protected bool StartProcess(IObserver<TConsoleOut> observer)
		{
			var started = false;
			try
			{
				started = this.Process.Start();
				if (started)
				{
					this.ProcessStarted(this.Process.StandardInput);
					return true;
				}

				OnError(observer, new ObservableProcessException($"Failed to start observable process: {this.Binary}"));
				return false;
			}
			catch (Exception e)
			{
				OnError(observer, new ObservableProcessException($"Exception while starting observable process: {this.Binary}", e.Message, e));
			}
			finally
			{
				if (!started) this.SetCompletedHandle();
			}

			return false;
		}

		protected virtual void OnError(IObserver<TConsoleOut> observer, Exception e) => observer.OnError(e);

		protected virtual void OnCompleted(IObserver<TConsoleOut> observer) => observer.OnCompleted();

		private readonly object _exitLock = new object();

		protected void OnExit(IObserver<TConsoleOut> observer)
		{
			if (!this.Started) return;
			ExitStop(observer);
		}

		private void ExitStop(IObserver<TConsoleOut> observer)
		{
			if (!this.Started) return;
			if (_isDisposing) return;
			lock (_exitLock)
			{
				if (!this.Started) return;

				this.Stop(observer);
			}
		}

		private Process CreateProcess()
		{
			var s = this._startArguments;
			var args = s.Args;
			var processStartInfo = new ProcessStartInfo
			{
				FileName = s.Binary,
				Arguments = args != null ? string.Join(" ", args) : string.Empty,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true
			};
			if (s.Environment != null)
				foreach (var kv in s.Environment)
					processStartInfo.Environment[kv.Key] = kv.Value;

			if (!string.IsNullOrWhiteSpace(s.WorkingDirectory)) processStartInfo.WorkingDirectory = s.WorkingDirectory;

			var p = new Process
			{
				EnableRaisingEvents = true,
				StartInfo = processStartInfo
			};
			return p;
		}

		protected virtual WaitHandle[] CompletionHandles()
		{
			return new WaitHandle[] {this._completedHandle};
		}

		/// <summary>
		/// Block until the process completes.
		/// </summary>
		/// <param name="timeout">The maximum time span we are willing to wait</param>
		/// <exception cref="CleanExitException">an exception that indicates a problem early in the pipeline</exception>
		public bool WaitForCompletion(TimeSpan timeout)
		{
			if (WaitHandle.WaitAll(CompletionHandles(), timeout)) return true;

			this.Stop();
			return false;
		}

		private readonly object _unpackLock = new object();
		private readonly object _sendLock = new object();
		private bool _sentControlC = false;

		public void SendControlC()
		{
			if (_sentControlC) return;
			if (!this.ProcessId.HasValue) return;
			var path = Path.Combine(Path.GetTempPath(), "proc-c.exe");
			this.UnpackTempOutOfProcessSignalSender(path);
			lock (_sendLock)
			{
				if (_sentControlC) return;
				if (!this.ProcessId.HasValue) return;
				var args = new StartArguments(path, this.ProcessId.Value.ToString(CultureInfo.InvariantCulture))
				{
					WaitForExit = null,
					CallingControlC = true
				};
				var result = Proc.Start(args, TimeSpan.FromSeconds(2));
				_sentControlC = true;
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

		protected bool StopRequested => _stopRequested || _sentControlC;
		private bool _stopRequested;
		private void Stop(IObserver<TConsoleOut> observer = null)
		{
			try
			{
				this._stopRequested = true;
				if (this.Process == null) return;

				var wait = this._startArguments.WaitForExit;
				try
				{
					if (this.Started && wait.HasValue)
					{
						bool exitted;
						if (this._startArguments.SendControlCFirst)
						{
							this.SendControlC();
							exitted = this.Process?.WaitForExit((int) wait.Value.TotalMilliseconds) ?? false;
							//still attempt to kill to process if control c failed
							if (!exitted) this.Process?.Kill();
						}
						else
						{
							this.Process?.Kill();
							exitted = this.Process?.WaitForExit((int) wait.Value.TotalMilliseconds) ?? false;
						}

						//we always need to do a hard wait for exit because the exit code might not be available otherwise
						//see: https://msdn.microsoft.com/en-us/library/system.diagnostics.process.exitcode(v=vs.110).aspx
						if (this.Process != null) this.HardWaitForExit(TimeSpan.FromSeconds(10));

					}
					else if (this.Started)
					{
						this.Process?.Kill();
						//we always need to do a hard wait for exit because the exit code might not be available otherwise
						//see: https://msdn.microsoft.com/en-us/library/system.diagnostics.process.exitcode(v=vs.110).aspx
						if (this.Process != null) this.HardWaitForExit(TimeSpan.FromSeconds(10));
					}
				}
				//Access denied usually means the program is already terminating.
				catch (Win32Exception)
				{
				}
				//This usually indiciates the process is already terminated
				catch (InvalidOperationException)
				{
				}

				var process = this.Process;
				if (!this.ExitCode.HasValue) this.ExitCode = process?.ExitCode;

				try
				{
					this.Process?.Dispose();
				}
				//the underlying call to .Close() can throw an NRE if you dispose too fast after starting
				catch (NullReferenceException)
				{
				}
			}
			finally
			{
				this.Started = false;
				if (observer != null) OnCompleted(observer);
				this.SetCompletedHandle();
			}
		}

		private void SetCompletedHandle()
		{
			OnBeforeSetCompletedHandle();
			this._completedHandle.Set();
		}

		protected virtual void OnBeforeSetCompletedHandle()
		{

		}



		private bool HardWaitForExit(TimeSpan timeSpan)
		{
			var task = Task.Run(() => this.Process.WaitForExit());
			return (Task.WaitAny(task, Task.Delay(timeSpan)) == 0);
		}

		private bool _isDisposed;
		private bool _isDisposing;

		public void Dispose()
		{
			this._isDisposing = true;
			this.Stop();
			this._isDisposed = true;
			this._isDisposing = false;
		}
	}
}
