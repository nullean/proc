using System;

namespace Elastic.ProcessManagement.Std
{
	public interface IConsoleOutWriter
	{
		void Write(Exception e);
		void Write(ConsoleOut consoleOut);
	}
}
