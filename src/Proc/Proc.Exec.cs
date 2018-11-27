using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ProcNet
{
	public static partial class Proc
	{

		/// <summary>
		/// This simply executes <paramref cref="binary"/> and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(string binary, params string[] arguments) => Exec(binary, DefaultTimeout, arguments);

		/// <summary>
		/// This simply executes <paramref cref="binary"/> and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(string binary, TimeSpan timeout, params string[] arguments) =>
			Exec(new ExecArguments(binary, arguments), timeout);

		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(ExecArguments arguments) => Exec(arguments, DefaultTimeout);

		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <param name="timeout">The maximum runtime of the started program</param>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static int Exec(ExecArguments arguments, TimeSpan timeout)
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

			using (var process = new Process())
			{
                process.StartInfo = info;
                if (!process.Start())
	                throw new Exception($"Failed to start \"{arguments.Binary} {args}\" {(pwd == null ? string.Empty : $"pwd: {pwd}")}");

                var completedBeforeTimeout = process.WaitForExit((int)timeout.TotalMilliseconds);
                if (!completedBeforeTimeout)
	                completedBeforeTimeout = HardWaitForExit(process, TimeSpan.FromSeconds(1));
                if (!completedBeforeTimeout)
	                throw new Exception($"Timeout {timeout} occured while running \"{arguments.Binary} {args}\" {(pwd == null ? string.Empty : $"pwd: {pwd}")}");
				return process.ExitCode;
			}

		}
		private static bool HardWaitForExit(Process process, TimeSpan timeSpan)
		{
			var task = Task.Run(() => process.WaitForExit());
			return (Task.WaitAny(task, Task.Delay(timeSpan)) == 0);
		}
	}
}
