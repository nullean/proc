using System;
using System.Threading;

namespace Elastic.ProcessManagement.Tests.Binary
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("no testcase specified");
				return 1;
			}

			var testCase = args[0].ToLowerInvariant();

			if (testCase == nameof(SingleLineNoEnter).ToLowerInvariant()) return SingleLineNoEnter();
			if (testCase == nameof(TwoWrites).ToLowerInvariant()) return TwoWrites();

			if (testCase == nameof(SingleLine).ToLowerInvariant()) return SingleLine();

			if (testCase == nameof(DelayedWriter).ToLowerInvariant()) return DelayedWriter();

			if (testCase == nameof(ReadKeyFirst).ToLowerInvariant()) return ReadKeyFirst();
			if (testCase == nameof(ReadKeyAfter).ToLowerInvariant()) return ReadKeyAfter();
			if (testCase == nameof(ReadLineFirst).ToLowerInvariant()) return ReadLineFirst();
			if (testCase == nameof(ReadLineAfter).ToLowerInvariant()) return ReadLineAfter();

			return 1;
		}
		private static int DelayedWriter()
		{
			Thread.Sleep(3000);
			Console.Write(nameof(DelayedWriter));
			return 20;
		}
		private static int SingleLineNoEnter()
		{
			Console.Write(nameof(SingleLineNoEnter));
			return 0;
		}
		private static int TwoWrites()
		{
			Console.Write(nameof(TwoWrites));
			Console.Write(nameof(TwoWrites));
			return 0;
		}
		private static int SingleLine()
		{
			Console.WriteLine(nameof(SingleLine));
			return 0;
		}
		private static int ReadKeyFirst()
		{
			var read = Convert.ToChar(Console.Read());
			Console.Write($"{read}{nameof(ReadKeyFirst)}");
			return 21;
		}
		private static int ReadKeyAfter()
		{
			Console.Write("input:");
			Console.Read();
			Console.Write(nameof(ReadKeyAfter));
			return 21;
		}
		private static int ReadLineFirst()
		{
			Console.ReadLine();
			Console.Write(nameof(ReadLineFirst));
			return 21;
		}
		private static int ReadLineAfter()
		{
			Console.Write("input:");
			Console.ReadLine();
			Console.Write(nameof(ReadLineAfter));
			return 21;
		}
	}
}
