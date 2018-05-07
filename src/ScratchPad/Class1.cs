using System;
using ProcNet;
using ProcNet.Std;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
			var result = Proc.Start(new StartArguments("ipconfig", "/all")
			{
				WorkingDirectory = @"c:\Projects\proc\src\Proc.Tests.Binary",
				WaitForStreamReadersTimeout = TimeSpan.FromMinutes(4)
			}, TimeSpan.FromMinutes(1), new ConsoleOutColorWriter());

			return 0;
		}

	}

	public class MyProcObservable : ObservableProcess
	{
		public MyProcObservable(string binary, params string[] arguments) : base(binary, arguments) { }

		public MyProcObservable(StartArguments startArguments) : base(startArguments) { }

		protected override bool BufferBoundary(char[] stdOut, char[] stdErr)
		{
			return base.BufferBoundary(stdOut, stdErr);
		}
	}
}
