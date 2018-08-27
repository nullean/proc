using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcNet
{
	public class StartArguments
	{
		public string Binary { get; }
		public IEnumerable<string> Args { get; }

		public StartArguments(string binary, IEnumerable<string> args) : this(binary, args?.ToArray()) { }

		public StartArguments(string binary, params string[] args)
		{
			this.Binary = binary;
			this.Args = args;
		}

		// ReSharper disable UnusedAutoPropertyAccessor.Global

		/// <summary>Provide environment variable scoped to the process being executed</summary>
		public IDictionary<string, string> Environment { get; set; }

		/// <summary> Set the current working directory</summary>
		public string WorkingDirectory { get; set; }

		/// <summary>
		/// By default processes are started in a threadpool thread assuming you start multiple from the same thread.
		/// By setting this property to true we do not do this. Know however that when multiple instances of observableprocess
		/// stop at the same time they are all queueing for <see cref="System.Diagnostics.Process.WaitForCompletion()"> which may lead to
		/// unexpected behaviour
		/// </summary>
		public bool NoWrapInThread { get; set; }

		public bool SendControlCFirst { get; set; }

		private static readonly TimeSpan DefaultWaitForExit = TimeSpan.FromSeconds(10);

		/// <summary>
		/// By default when we kill the process we wait for its completion with a timeout of `10s`.
		/// By specifying `null` you omit the wait for completion alltogether.
		/// </summary>
		public TimeSpan? WaitForExit { get; set; } = DefaultWaitForExit;

		/// <summary>
		/// How long we should wait for the output stream readers to finish when the process exits before we call
		/// <see cref="ObservableProcessBase{TConsoleOut}.OnCompleted"/> is called. By default waits for 5 seconds.
		/// </summary>
		public TimeSpan WaitForStreamReadersTimeout { get; set; } = DefaultWaitForExit;

		// ReSharper enable UnusedAutoPropertyAccessor.Global

	}
}
