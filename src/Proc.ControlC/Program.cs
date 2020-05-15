using System;
using System.ComponentModel;

namespace Proc.ControlC
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			Console.WriteLine("Calling" + string.Join(" ", args));
			if (args.Length == 0) return 1;
			if (!int.TryParse(args[0], out var processId)) return 2;

			try
			{
				var success = ControlCDispatcher.Send(processId) ? 0 : 1;
				Console.WriteLine("success: " + success);
				return success;
			}
			catch (Win32Exception e)
			{
				Console.WriteLine(e.Message);
				return 3;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return 4;
			}

		}
	}
}
