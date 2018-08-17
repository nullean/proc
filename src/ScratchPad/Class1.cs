using System;
using System.Threading.Tasks;
using ProcNet;
using ProcNet.Std;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
			var tessCase = TestBinary.TestCaseArguments("MoreText");
			var process = new ObservableProcess(tessCase);
			process.SubscribeLines(c =>
			{
				if (c.Line.EndsWith("3"))
				{
					process.CancelAsyncReads();
					Task.Run(async () =>
					{
						await Task.Delay(TimeSpan.FromSeconds(2));
						process.StartAsyncReads();
					});
				}
				Console.WriteLine(c.Line);
			}, e=> Console.Error.WriteLine(e));


			process.WaitForCompletion(TimeSpan.FromSeconds(20));
			Console.WriteLine("exitCode:" + process.ExitCode);

//			var result = Proc.Start(new StartArguments("ipconfig", "/all")
//			{
//				WorkingDirectory = @"c:\Projects\proc\src\Proc.Tests.Binary",
//				WaitForStreamReadersTimeout = TimeSpan.FromMinutes(4)
//			}, TimeSpan.FromMinutes(1), new ConsoleOutColorWriter());

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
