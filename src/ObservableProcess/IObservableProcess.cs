using System;
using System.Collections.Generic;
using Elastic.ProcessManagement.Std;

namespace Elastic.ProcessManagement
{
	public interface ISubscribeLines
	{
		IDisposable Subscribe(IObserver<LineOut> observer);
	}


	public interface IObservableProcess<out TConsoleOut> : IDisposable, IObservable<TConsoleOut>
		where TConsoleOut : ConsoleOut
	{
		/// <summary>
		/// <see cref="IConsoleOutWriter"/> is a handy abstraction if all you want to do is redirect output to the console.
		/// This library ships with <see cref="ConsoleOutWriter"/> and <see cref="ConsoleOutColorWriter"/>.
		/// </summary>
		IDisposable Subscribe(IConsoleOutWriter writer);

		/// <summary>
		/// When the process exits this will indicate its status code.
		/// </summary>
		int? ExitCode { get; }

		/// <summary>
		/// The process id of the started process
		/// </summary>
		int? ProcessId { get; }
	}
}
