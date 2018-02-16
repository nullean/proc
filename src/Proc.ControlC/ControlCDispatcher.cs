using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using static System.Diagnostics.Process;

namespace Proc.ControlC
{
	[SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
	internal static class ControlCDispatcher
	{
		private const int CTRL_C_EVENT = 0;

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AttachConsole(uint dwProcessId);

		[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
		private static extern bool FreeConsole();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

		private delegate bool ConsoleCtrlDelegate(uint CtrlType);

		public static bool Send(int processId)
		{
			if (!IsAProcessAndIsRunning(processId)) return false;

			if (!FreeConsole()) throw new Win32Exception();
			if (!AttachConsole((uint) processId)) throw new Win32Exception();

			if (!SetConsoleCtrlHandler(null, true)) throw new Win32Exception();
			try
			{
				if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)) throw new Win32Exception();
			}
			finally
			{
				FreeConsole();
				SetConsoleCtrlHandler(null, false);
			}
			return true;
		}

		private static bool IsAProcessAndIsRunning(int processId)
		{
			try
			{
				var p = GetProcessById(processId);
				Console.WriteLine(p.ProcessName);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
