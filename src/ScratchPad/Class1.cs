using System;
using ProcNet;

namespace ScratchPad
{
	public static class Program
	{
		public static int Main()
		{
			var args = new StartArguments("ipconfig", "/all")
			{
				SendControlCFirst = true
			};

			using (var p = new ObservableProcess(args))
			{
				p.Subscribe(c => Console.Write(c.Characters));
				p.SubscribeLines(l => Console.WriteLine(l.Line));

				p.WaitForCompletion(TimeSpan.FromSeconds(2));

			}
			Console.WriteLine("done");

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
