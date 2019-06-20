using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Std;

namespace ProcNet
{
	public delegate void StartedHandler(StreamWriter standardInput);

	public abstract class ObservableProcessBase<TConsoleOut> : IObservableProcess<TConsoleOut>
		where TConsoleOut : ConsoleOut
	{
		protected ObservableProcessBase(string binary, params string[] arguments)
			: this(new StartArguments(binary, arguments)) { }

		protected ObservableProcessBase(StartArguments startArguments)
		{
			this.StartArguments = startArguments ?? throw new ArgumentNullException(nameof(startArguments));
			this.Process = CreateProcess();
			this.CreateObservable();
		}

		public virtual IDisposable Subscribe(IObserver<TConsoleOut> observer) => this.OutStream.Subscribe(observer);

		public IDisposable Subscribe(IConsoleOutWriter writer) => this.OutStream.Subscribe(writer.Write, writer.Write, delegate { });

		private readonly ManualResetEvent _completedHandle = new ManualResetEvent(false);

		public StreamWriter StandardInput => this.Process.StandardInput;
		public string Binary => this.StartArguments.Binary;
		public int? ExitCode { get; private set; }

		protected StartArguments StartArguments { get; }
		protected Process Process { get; }
		protected bool Started { get; set; }
		protected string ProcessName { get; private set; }

		protected bool NoWrapInThread => this.StartArguments.NoWrapInThread;
		private int? _processId;
		public virtual int? ProcessId => _processId;

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
					try
					{
						this._processId = this.Process.Id;
						this.ProcessName = this.Process.ProcessName;
					}
					catch (InvalidOperationException)
					{
						// best effort, Process could have finished before even attempting to read .Id and .ProcessName
						// which can throw if the process exits in between
					}
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

		protected virtual void OnError(IObserver<TConsoleOut> observer, Exception e)
		{
			this.HardKill();
			observer.OnError(e);
		}

		protected virtual void OnCompleted(IObserver<TConsoleOut> observer) => observer.OnCompleted();

		private readonly object _exitLock = new object();

		protected void OnExit(IObserver<TConsoleOut> observer)
		{
			if (!this.Started) return;
			int? exitCode = null;
			try
			{
				exitCode = this.Process.ExitCode;
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
			if (!this.Started) return;
			if (_isDisposing) return;
			lock (_exitLock)
			{
				if (!this.Started) return;

				this.Stop(exitCode, observer);
			}
		}

		private Process CreateProcess()
		{
			var s = this.StartArguments;
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
			{
				foreach (var kv in s.Environment)
				{
		#if NET45
					processStartInfo.EnvironmentVariables[kv.Key] = kv.Value;
		#else
					processStartInfo.Environment[kv.Key] = kv.Value;
		#endif
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
		/// <exception cref="CleanExitExceptionBase">an exception that indicates a problem early in the pipeline</exception>
		public bool WaitForCompletion(TimeSpan timeout)
		{
			if (this._completedHandle.WaitOne(timeout)) return true;

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
			var platform = (int)Environment.OSVersion.Platform;
			var isWindows = platform != 4 && platform != 6 && platform != 128;
			if (isWindows)
			{
				var path = Path.Combine(Path.GetTempPath(), "proc-c.exe");
				this.UnpackTempOutOfProcessSignalSender(path);
				lock (_sendLock)
				{
					if (_sentControlC) return;
					if (!this.ProcessId.HasValue) return;
					var args = new StartArguments(path, this.ProcessId.Value.ToString(CultureInfo.InvariantCulture))
					{
						WaitForExit = null,
					};
					var result = Proc.Start(args, TimeSpan.FromSeconds(2));
					_sentControlC = true;
					this.SendYesForBatPrompt();
				}
			}
			else
			{
				lock (_sendLock)
				{
					if (_sentControlC) return;
					if (!this.ProcessId.HasValue) return;
					// I wish .NET Core had signals baked in but looking at the corefx repos tickets this is not happening any time soon.
					var args = new StartArguments("kill", "-SIGINT", this.ProcessId.Value.ToString(CultureInfo.InvariantCulture))
					{
						WaitForExit = null,
					};
					var result = Proc.Start(args, TimeSpan.FromSeconds(2));
					_sentControlC = true;
				}
				
			}
			
			
		}

		protected void SendYesForBatPrompt()
		{
			if (!this.StopRequested) return;
			if (this.ProcessName == "cmd")
			{
				try
				{
					this.StandardInput.WriteLine("Y");
				}
				//best effort
				catch (InvalidOperationException) { }
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
		private void Stop(int? exitCode = null, IObserver<TConsoleOut> observer = null)
		{
			try
			{
				this._stopRequested = true;
				if (this.Process == null) return;

				var wait = this.StartArguments.WaitForExit;
				try
				{
					if (this.Started && wait.HasValue)
					{
						bool exitted;
						if (this.StartArguments.SendControlCFirst)
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

						//if we haven't exited do a hard wait for exit by using the overload that does not timeout.
						if (this.Process != null && !exitted) this.HardWaitForExit(TimeSpan.FromSeconds(10));
					}
					else if (this.Started)
					{
						this.Process?.Kill();
					}
				}
				//Access denied usually means the program is already terminating.
				catch (Win32Exception) { }
				//This usually indiciates the process is already terminated
				catch (InvalidOperationException) { }
				try
				{
					this.Process?.Dispose();
				}
				//the underlying call to .Close() can throw an NRE if you dispose too fast after starting
				catch (NullReferenceException) { }
			}
			finally
			{
				if (this.Started && exitCode.HasValue)
					this.ExitCode = exitCode.Value;

				this.Started = false;
				if (observer != null) OnCompleted(observer);
				this.SetCompletedHandle();
			}
		}

		private void HardKill()
		{
			try
			{
				this.Process?.Kill();
			}
			catch (Exception)
			{
				// ignored
			}
			finally
			{
				try
				{
					this.Process?.Dispose();
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
			this._completedHandle.Set();
		}

		protected virtual void OnBeforeSetCompletedHandle() { }

		private bool HardWaitForExit(TimeSpan timeSpan)
		{
			var task = Task.Run(() => this.Process.WaitForExit());
			return (Task.WaitAny(task, Task.Delay(timeSpan)) == 0);
		}

		private bool _isDisposing;

		public void Dispose()
		{
			this._isDisposing = true;
			this.Stop();
			this._isDisposing = false;
		}
	}
}
