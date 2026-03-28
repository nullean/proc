using System;
using System.Diagnostics;
using System.Linq;
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
			var args = arguments.Args?.ToArray() ?? [];
			var info = new ProcessStartInfo(arguments.Binary)
			{
				UseShellExecute = false
			};
			foreach (var arg in args)
				info.ArgumentList.Add(arg);

			var pwd = arguments.WorkingDirectory;
			if (!string.IsNullOrWhiteSpace(pwd)) info.WorkingDirectory = pwd;
			if (arguments.Environment != null)
				foreach (var kv in arguments.Environment)
					info.Environment[kv.Key] = kv.Value;

			var printBinary = arguments.OnlyPrintBinaryInExceptionMessage
				? $"\"{arguments.Binary}\""
				: $"\"{arguments.Binary} {args.NaivelyQuoteArguments()}\"{(pwd == null ? string.Empty : $" pwd: {pwd}")}";

			using var process = new Process { StartInfo = info };
			if (!process.Start()) throw new ProcExecException($"Failed to start {printBinary}");

			try
			{
				if (arguments.Timeout.HasValue)
				{
					using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx);
					linked.CancelAfter(arguments.Timeout.Value);
					try
					{
						await process.WaitForExitAsync(linked.Token);
					}
					catch (OperationCanceledException) when (!ctx.IsCancellationRequested)
					{
						// Timeout fired (not the caller's token) — kill the process and report timeout.
						await KillProcessAsync(process);
						throw new ProcExecException($"Timeout {arguments.Timeout.Value} occured while running {printBinary}");
					}
				}
				else
					await process.WaitForExitAsync(ctx);
			}
			catch (OperationCanceledException)
			{
				// Caller cancelled — kill the process so it does not become an orphan.
				await KillProcessAsync(process);
				throw;
			}

			var exitCode = process.ExitCode;
			if (!arguments.ValidExitCodeClassifier(exitCode))
				throw new ProcExecException($"Process exited with '{exitCode}' {printBinary}")
				{
					ExitCode = exitCode
				};

			return exitCode;
		}

		private static async Task KillProcessAsync(Process process)
		{
			try
			{
				process.Kill(entireProcessTree: true);
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
				await process.WaitForExitAsync(cts.Token);
			}
			catch { /* best effort */ }
		}
	}
}
#endif
