using System;

namespace ProcNet.Std
{
	public interface IConsoleLineHandler
	{
		void Handle(LineOut lineOut);
		void Handle(Exception e);
	}
	
}
