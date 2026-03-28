using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ProcNet.Extensions
{
	internal static class ProcessWaitExtensions
	{
		/// <summary>
		/// Calls the no-arg <see cref="Process.WaitForExit()"/> overload (which drains redirected
		/// streams) guarded by <see cref="Task.Run(Action)"/> + <see cref="Task.WaitAny(Task[])"/>
		/// so the caller is never blocked forever if the process never exits.
		/// </summary>
		/// <returns>True if the process exited within <paramref name="timeSpan"/>.</returns>
		internal static bool HardWaitForExit(this Process process, TimeSpan timeSpan)
		{
			var task = Task.Run(() => process.WaitForExit());
			return Task.WaitAny(task, Task.Delay(timeSpan)) == 0;
		}
	}
}
