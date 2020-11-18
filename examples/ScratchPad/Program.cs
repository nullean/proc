using System;
using ProcNet;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
			var tessCase = TestBinary.TestCaseArguments("ControlC");
			var process = new ObservableProcess(tessCase);
			process.SubscribeLines(c =>
			{
				//if (c.Line.EndsWith("4"))
				{
					process.SendControlC();
				}
				Console.WriteLine(c.Line);
			}, e=> Console.Error.WriteLine(e));

			process.WaitForCompletion(TimeSpan.FromSeconds(20));
			return 0;
		}

	}

	public class MyProcObservable : ObservableProcess
	{
		public MyProcObservable(string binary, params string[] arguments) : base(binary, arguments) { }

		public MyProcObservable(StartArguments startArguments) : base(startArguments) { }

		protected override bool BufferBoundary(char[] stdOut, char[] stdErr) => base.BufferBoundary(stdOut, stdErr);
	}
}
