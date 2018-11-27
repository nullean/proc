using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ProcNet;
using ProcNet.Std;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
//			var tessCase = TestBinary.TestCaseArguments("SlowOutput");
//			var process = new ObservableProcess(tessCase);
//			process.SubscribeLines(c =>
//			{
//				if (c.Line.EndsWith("3"))
//				{
//					process.CancelAsyncReads();
//					Task.Run(async () =>
//					{
//						await Task.Delay(TimeSpan.FromSeconds(2));
//						process.StartAsyncReads();
//					});
//				}
//				Console.WriteLine(c.Line);
//			}, e=> Console.Error.WriteLine(e));
//
//
//			process.WaitForCompletion(TimeSpan.FromSeconds(20));
//			Console.WriteLine("exitCode:" + process.ExitCode);
			var info = new ProcessStartInfo("git.exe", "status");
			info.WorkingDirectory = @"c:\projects\nullean\proc";
			info.UseShellExecute = false;
			var proc = Process.Start(info);
			proc.WaitForExit();

			var result = Proc.Start(new StartArguments("git", "status")
			{
				WorkingDirectory = @"c:\projects\nullean\proc"
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
