using System;

namespace ProcNet.Std;

public class ConsoleOutColorWriter : ConsoleOutWriter
{
	public static ConsoleOutColorWriter Default { get; } = new();

	private readonly object _lock = new();
	public override void Write(ConsoleOut consoleOut)
	{
		lock (_lock)
		{
			Console.ResetColor();
			if (consoleOut.Error)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				consoleOut.CharsOrString(ErrorCharacters, ErrorLine);
			}
			else
				consoleOut.CharsOrString(OutCharacters, OutLine);

			Console.ResetColor();
		}
	}

	public override void Write(Exception e)
	{
		if (!(e is CleanExitExceptionBase ee)) throw e;
		lock (_lock)
		{
			Console.ResetColor();
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
