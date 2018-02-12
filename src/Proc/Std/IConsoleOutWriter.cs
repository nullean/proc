using System;

namespace ProcNet.Std
{
	public interface IConsoleOutWriter
	{
		void Write(Exception e);
		void Write(ConsoleOut consoleOut);
	}
}
