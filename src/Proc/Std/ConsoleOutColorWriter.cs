using System;

namespace ProcNet.Std
{
	public class ConsoleOutColorWriter : ConsoleOutWriter
	{
		public static ConsoleOutColorWriter Default { get; } = new ConsoleOutColorWriter();

		private readonly object _lock = new object();
		public override void Write(ConsoleOut consoleOut)
		{
			lock (_lock)
			{
				if (consoleOut.Error)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					consoleOut.CharsOrString(ErrorCharacters, ErrorLine);
				}
				else
				{
					Console.ResetColor();
					consoleOut.CharsOrString(OutCharacters, OutLine);
				}
				Console.ResetColor();
			}
		}

		public override void Write(Exception e)
		{
			if (!(e is CleanExitExceptionBase ee)) throw e;
			lock (_lock)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write(e.Message);
				if (!string.IsNullOrEmpty(ee.HelpText))
				{
					Console.ForegroundColor = ConsoleColor.DarkRed;
					Console.Write(ee.HelpText);
				}
				Console.ResetColor();
			}
		}
	}
}
