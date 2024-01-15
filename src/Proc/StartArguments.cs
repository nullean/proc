using System;
using System.Collections.Generic;
using ProcNet.Std;

namespace ProcNet
{
	/// <summary> Encompasses all the options you can specify to start Proc processes </summary>
	public class StartArguments : ProcessArgumentsBase
	{
		public StartArguments(string binary, IEnumerable<string> args) : base(binary, args) { }

		public StartArguments(string binary, params string[] args) : base(binary, args) { }

		// ReSharper disable UnusedAutoPropertyAccessor.Global


		/// <summary>
		/// By default processes are started in a threadpool thread assuming you start multiple from the same thread.
		/// By setting this property to true we do not do this. Know however that when multiple instances of observableprocess
		/// stop at the same time they are all queueing for <see cref="System.Diagnostics.Process.WaitForCompletion()"> which may lead to
		/// unexpected behaviour
		/// </summary>
		public bool NoWrapInThread { get; set; }

		/// <summary>
		/// return true to stop buffering lines. Allows callers to optionally short circuit reading
		/// from stdout/stderr without closing the process.
		/// This is great for long running processes to only buffer console output until
		/// all information is parsed.
		/// </summary>
		/// <returns>True to end the buffering of char[] to lines of text</returns>
		public Func<LineOut, bool> KeepBufferingLines { get; set; }

		/// <summary> Attempts to send control+c (SIGINT) to the process first </summary>
		public bool SendControlCFirst { get; set; }

		/// <summary>
		/// Filter the lines we are interesting in and want to return on <see cref="ProcessCaptureResult.ConsoleOut"/>
		/// Defaults to true meaning all all lines.
		/// </summary>
		public Func<LineOut, bool> LineOutFilter { get; set; }

		/// <summary>
		/// Callback with a reference to standard in once the process has started and stdin can be written too
		/// </summary>
		public StandardInputHandler StandardInputHandler { get; set; }

		private static readonly TimeSpan DefaultWaitForExit = TimeSpan.FromSeconds(10);

		/// <summary>
		/// By default when we kill the process we wait for its completion with a timeout of `10s`.
		/// By specifying `null` you omit the wait for completion all together.
		/// </summary>
		public TimeSpan? WaitForExit { get; set; } = DefaultWaitForExit;

		/// <summary>
		/// How long we should wait for the output stream readers to finish when the process exits before we call
		/// <see cref="ObservableProcessBase{TConsoleOut}.OnCompleted"/> is called. By default waits for 5 seconds.
		/// <para>Set to null to skip waiting,defaults to 10 seconds</para>
		/// </summary>
		public TimeSpan? WaitForStreamReadersTimeout { get; set; } = DefaultWaitForExit;

		// ReSharper enable UnusedAutoPropertyAccessor.Global

	}
}
