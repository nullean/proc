using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ProcNet.Extensions;

namespace ProcNet
{
	public static partial class Proc
	{

		/// <summary>
		/// This simply executes <paramref name="binary"/> and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(string binary, params string[] arguments) => Exec(new ExecArguments(binary, arguments));

		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(ExecArguments arguments)
		{
			var args = arguments.Args.NaivelyQuoteArguments();
			var info = new ProcessStartInfo(arguments.Binary)
			{
				UseShellExecute = false
			};
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
			foreach (var arg in arguments.Args)
				info.ArgumentList.Add(arg);
#else
			info.Arguments = args;
#endif

			var pwd = arguments.WorkingDirectory;
			if (!string.IsNullOrWhiteSpace(pwd)) info.WorkingDirectory = pwd;
			if (arguments.Environment != null)
				foreach (var kv in arguments.Environment)
					info.Environment[kv.Key] = kv.Value;

			var printBinary = arguments.OnlyPrintBinaryInExceptionMessage
				? $"\"{arguments.Binary}\""
				: $"\"{arguments.Binary} {args}\"{(pwd == null ? string.Empty : $" pwd: {pwd}")}";

			using var process = new Process { StartInfo = info };
			if (!process.Start())
				throw new ProcExecException($"Failed to start {printBinary}");

			if (arguments.Timeout.HasValue)
			{
				var t = arguments.Timeout.Value;
				var completedBeforeTimeout =process.WaitForExit((int)t.TotalMilliseconds);
				if (!completedBeforeTimeout)
				{
					HardWaitForExit(process, TimeSpan.FromSeconds(1));
					throw new ProcExecException($"Timeout {t} occured while running {printBinary}");
				}
			}
			else
				process.WaitForExit();

			var exitCode = process.ExitCode;
			if (!arguments.ValidExitCodeClassifier(exitCode))
				throw new ProcExecException($"Process exited with '{exitCode}' {printBinary}")
				{
					ExitCode = exitCode
				};

			return exitCode;
		}

		private static void HardWaitForExit(Process process, TimeSpan timeSpan)
		{
			using var task = Task.Run(() => process.WaitForExit());
			Task.WaitAny(task, Task.Delay(timeSpan));
		}
	}
}
