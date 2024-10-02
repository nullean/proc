using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ProcNet.Extensions;

#if NET6_0_OR_GREATER
namespace ProcNet
{
	public static partial class Proc
	{
		/// <summary>
		/// This simply executes a binary and returns the exit code or throws if the binary failed to start
		/// <para>This method shares the same console and does not capture the output</para>
		/// <para>Use <see cref="Start(string,string[])"/> or overloads if you want to capture output and write to console in realtime</para>
		/// </summary>
		/// <exception cref="Exception">If the application fails to start</exception>
		/// <returns>The exit code of the binary being run</returns>
		public static async Task<int> ExecAsync(ExecArguments arguments, CancellationToken ctx = default)
		{
			var args = arguments.Args.NaivelyQuoteArguments();
			var info = new ProcessStartInfo(arguments.Binary)
			{
				UseShellExecute = false
			};
			foreach (var arg in arguments.Args)
				info.ArgumentList.Add(arg);

			var pwd = arguments.WorkingDirectory;
			if (!string.IsNullOrWhiteSpace(pwd)) info.WorkingDirectory = pwd;
			if (arguments.Environment != null)
				foreach (var kv in arguments.Environment)
					info.Environment[kv.Key] = kv.Value;

			var printBinary = arguments.OnlyPrintBinaryInExceptionMessage
				? $"\"{arguments.Binary}\""
				: $"\"{arguments.Binary} {args}\"{(pwd == null ? string.Empty : $" pwd: {pwd}")}";

			using var process = new Process { StartInfo = info };
			if (!process.Start()) throw new ProcExecException($"Failed to start {printBinary}");

			if (arguments.Timeout.HasValue)
			{
				var t = arguments.Timeout.Value;
				var completedBeforeTimeout =process.WaitForExit((int)t.TotalMilliseconds);
				if (!completedBeforeTimeout)
				{
					await HardWaitForExitAsync(process, TimeSpan.FromSeconds(1));
					throw new ProcExecException($"Timeout {t} occured while running {printBinary}");
				}
			}
			else
				await process.WaitForExitAsync(ctx);

			var exitCode = process.ExitCode;
			if (!arguments.ValidExitCodeClassifier(exitCode))
				throw new ProcExecException($"Process exited with '{exitCode}' {printBinary}")
				{
					ExitCode = exitCode
				};

			return exitCode;
		}

		private static async Task HardWaitForExitAsync(Process process, TimeSpan timeSpan)
		{
			using var task = Task.Run(() => process.WaitForExitAsync());
			await Task.WhenAny(task, Task.Delay(timeSpan));
		}
	}
}
#endif
