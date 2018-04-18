using System;
using ProcNet;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
			var result = Proc.Start(new StartArguments("dotnet", "run", "intermixedoutanderror")
			{
				WorkingDirectory = @"c:\Projects\proc\src\Proc.Tests.Binary"
			});

			Console.WriteLine(result);

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
