using System;

namespace Proc.Std
{
	public interface IConsoleOutWriter
	{
		void Write(Exception e);
		void Write(ConsoleOut consoleOut);
	}
}
