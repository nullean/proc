namespace Proc.ControlC
{
	class Program
	{
		static int Main(string[] args)
		{
			if (args.Length == 0) return 1;
			if (!int.TryParse(args[0], out int processId)) return 2;

			//if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return ControlCDispatcher.Send(processId) ? 0 : 1;

		}
	}
}
