using System;
using System.Collections.Generic;
using ProcNet.Std;

namespace ProcNet;

public class LongRunningArguments : StartArguments
{
	public LongRunningArguments(string binary, IEnumerable<string> args) : base(binary, args) { }

	public LongRunningArguments(string binary, params string[] args) : base(binary, args) { }

	/// <summary>
	/// A handler that will delay return of the <see cref="IDisposable"/> process until startup is confirmed over
	/// standard out/error.
	/// </summary>
	public Func<LineOut, bool> StartedConfirmationHandler { get; set; }

	/// <summary>
	/// A helper that sets <see cref="StartArguments.KeepBufferingLines"/> and stops immediately after <see cref="StartedConfirmationHandler"/>
	/// indicates the process has started.
	/// </summary>
	public bool StopBufferingAfterStarted { get; set; }
}
