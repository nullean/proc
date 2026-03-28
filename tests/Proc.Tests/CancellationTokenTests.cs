using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	public class CancellationTokenTests : TestsBase
	{
		// ── Proc.Start ────────────────────────────────────────────────────────────

		[Fact]
		public void Start_WhenCancelled_StopsPromptlyAndThrows()
		{
			// Before fix: Start() had no CancellationToken parameter — callers had no way
			// to abort a long-running process other than setting a Timeout on StartArguments.
			// After fix: Start(args, ct) stops the process and throws OperationCanceledException.
			var args = TestCaseArguments("SlowOutput"); // runs for ~5 seconds
			args.Timeout = null; // remove the default 5s timeout so only ct drives the abort

			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
			var sw = Stopwatch.StartNew();

			Action call = () => Proc.Start(args, cts.Token);
			call.Should().Throw<OperationCanceledException>();

			sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
				"cancellation should abort the wait well before SlowOutput's 5-second run");
		}

		// ── Proc.StartRedirected ──────────────────────────────────────────────────

		[Fact]
		public void StartRedirected_WhenCancelled_StopsPromptlyAndThrows()
		{
			// Same design gap as Start — no CancellationToken support.
			var args = TestCaseArguments("SlowOutput");
			args.Timeout = null;

			var lines = new List<LineOut>();
			var handler = new CapturingHandler(lines);

			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
			var sw = Stopwatch.StartNew();

			Action call = () => Proc.StartRedirected(args, handler, cts.Token);
			call.Should().Throw<OperationCanceledException>();

			sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
		}

		// ── Proc.StartLongRunning ─────────────────────────────────────────────────

		[Fact]
		public void StartLongRunning_WhenCancelledDuringStartupWait_ThrowsAndCleansUp()
		{
			// Before fix: StartLongRunning() had no CancellationToken — the only escape from
			// the startup confirmation wait was for it to time out.
			// After fix: pass ct; if cancelled during the confirmation wait the subscription
			// is disposed and OperationCanceledException is thrown.
			var args = LongRunningTestCaseArguments("TrulyLongRunning"); // takes ~2s to print "Started!"
			args.StartedConfirmationHandler = l => l.Line == "Started!";

			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
			var sw = Stopwatch.StartNew();

			Action call = () => Proc.StartLongRunning(args, WaitTimeout, ct: cts.Token);
			call.Should().Throw<OperationCanceledException>();

			sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
				"cancellation should abort the confirmation wait early");
		}

		// ── WaitForCompletion ─────────────────────────────────────────────────────

		[Fact]
		public void WaitForCompletion_WhenCancelled_ThrowsAndStopsProcess()
		{
			// Direct test of the WaitForCompletion(TimeSpan?, CancellationToken) overload.
			var process = new ObservableProcess(TestCaseArguments("SlowOutput"));
			var lines = new List<string>();
			process.SubscribeLines(l => lines.Add(l.Line));

			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
			var sw = Stopwatch.StartNew();

			Action wait = () => process.WaitForCompletion(null, cts.Token);
			wait.Should().Throw<OperationCanceledException>();

			sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
				"process should be stopped promptly when the cancellation token fires");
			process.ExitCode.Should().HaveValue("Stop() should have been called, giving the process an exit code");
		}

		private class CapturingHandler : IConsoleLineHandler
		{
			private readonly List<LineOut> _lines;
			public CapturingHandler(List<LineOut> lines) => _lines = lines;
			public void Handle(LineOut line) => _lines.Add(line);
			public void Handle(Exception e) { }
		}
	}
}
