using System;
using System.Diagnostics;
using System.Threading.Tasks;

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
		public static void Exec(string binary, params string[] arguments) => Exec(binary, DefaultTimeout, arguments);

		/// <summary>
		/// This simply executes <paramref name="binary"/> and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static void Exec(string binary, TimeSpan timeout, params string[] arguments) =>
			Exec(new ExecArguments(binary, arguments), timeout);

		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static void Exec(ExecArguments arguments) => Exec(arguments, DefaultTimeout);

		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static void Exec(ExecArguments arguments, TimeSpan timeout)
		{
			var args = string.Join(" ", arguments.Args ?? Array.Empty<string>());
			var info = new ProcessStartInfo(arguments.Binary, args)
			{
				UseShellExecute = false
			};

			var pwd = arguments.WorkingDirectory;
			if (!string.IsNullOrWhiteSpace(pwd)) info.WorkingDirectory = pwd;
			if (arguments.Environment != null)
				foreach (var kv in arguments.Environment)
					info.Environment[kv.Key] = kv.Value;

			var printBinary = arguments.OnlyPrintBinaryInExceptionMessage
				? $"\"{arguments.Binary}\""
				: $"\"{arguments.Binary} {args}\"{(pwd == null ? string.Empty : $" pwd: {pwd}")}";

			using (var process = new Process())
			{
                process.StartInfo = info;
                if (!process.Start()) throw new ProcExecException($"Failed to start {printBinary}");

                var completedBeforeTimeout = process.WaitForExit((int)timeout.TotalMilliseconds);
                if (!completedBeforeTimeout)
                {
	                HardWaitForExit(process, TimeSpan.FromSeconds(1));
	                throw new ProcExecException($"Timeout {timeout} occured while running {printBinary}");
                }

                var exitCode = process.ExitCode;
                if (!arguments.ValidExitCodeClassifier(exitCode))
	                throw new ProcExecException($"Process exited with '{exitCode}' {printBinary}")
	                {
		                ExitCode = exitCode
	                };
			}

		}
		private static void HardWaitForExit(Process process, TimeSpan timeSpan)
		{
			var task = Task.Run(() => process.WaitForExit());
			Task.WaitAny(task, Task.Delay(timeSpan));
		}
	}
}
