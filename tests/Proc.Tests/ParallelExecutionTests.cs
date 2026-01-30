using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using ProcNet.Std;
using Xunit;

namespace ProcNet.Tests
{
	/// <summary>
	/// Tests that verify processes can be started and run in parallel without race conditions.
	/// These tests ensure the removal of Task.Run wrapping doesn't break parallel execution.
	/// </summary>
	public class ParallelExecutionTests : TestsBase
	{
		private const int ParallelCount = 3;
		private const string SingleLineTestCase = "SingleLine";
		private const string SlowOutputTestCase = "SlowOutput";

		// Longer timeout for parallel tests - processes compete for resources
		private static readonly TimeSpan ParallelTimeout = TimeSpan.FromSeconds(30);

		[Fact]
		public async Task ObservableProcess_CanRunInParallel()
		{
			var results = new ConcurrentBag<(int index, List<string> lines, int? exitCode)>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var seen = new List<string>();
				var process = new ObservableProcess(TestCaseArguments(SingleLineTestCase));
				process.SubscribeLines(c => seen.Add(c.Line));
				process.WaitForCompletion(ParallelTimeout);
				results.Add((i, seen, process.ExitCode));
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var (index, lines, exitCode) in results)
			{
				exitCode.Should().Be(0, $"process {index} should exit with code 0");
				lines.Should().ContainSingle().Which.Should().Be(SingleLineTestCase);
			}
		}

		[Fact]
		public async Task EventBasedObservableProcess_CanRunInParallel()
		{
			var results = new ConcurrentBag<(int index, List<string> lines, int? exitCode)>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var seen = new List<string>();
				var process = new EventBasedObservableProcess(TestCaseArguments(SingleLineTestCase));
				process.Subscribe(c => seen.Add(c.Line));
				process.WaitForCompletion(ParallelTimeout);
				results.Add((i, seen, process.ExitCode));
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var (index, lines, exitCode) in results)
			{
				exitCode.Should().Be(0, $"process {index} should exit with code 0");
				lines.Should().ContainSingle().Which.Should().Be(SingleLineTestCase);
			}
		}

		[Fact]
		public async Task ProcStart_CanRunInParallel()
		{
			var results = new ConcurrentBag<ProcessCaptureResult>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var args = TestCaseArguments(SingleLineTestCase);
				args.Timeout = ParallelTimeout;
				args.ConsoleOutWriter = new NoopConsoleOutWriter();
				var result = Proc.Start(args);
				results.Add(result);
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var result in results)
			{
				result.Completed.Should().BeTrue();
				result.ExitCode.Should().Be(0);
				result.ConsoleOut.Should().ContainSingle().Which.Line.Should().Be(SingleLineTestCase);
			}
		}

		[Fact]
		public async Task ProcStartRedirected_CanRunInParallel()
		{
			var results = new ConcurrentBag<(ProcessResult result, List<string> lines)>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var lines = new List<string>();
				var handler = new CollectingLineHandler(lines);
				var args = TestCaseArguments(SingleLineTestCase);
				args.Timeout = ParallelTimeout;
				var result = Proc.StartRedirected(args, handler);
				results.Add((result, lines));
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var (result, lines) in results)
			{
				result.Completed.Should().BeTrue();
				result.ExitCode.Should().Be(0);
				lines.Should().ContainSingle().Which.Should().Be(SingleLineTestCase);
			}
		}

		[Fact]
		public async Task MixedProcessTypes_CanRunInParallel()
		{
			var observableResults = new ConcurrentBag<int?>();
			var eventBasedResults = new ConcurrentBag<int?>();
			var procStartResults = new ConcurrentBag<int?>();

			var tasks = new List<Task>();

			// Start ObservableProcess instances
			tasks.AddRange(Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var process = new ObservableProcess(TestCaseArguments(SingleLineTestCase));
				process.SubscribeLines(_ => { });
				process.WaitForCompletion(ParallelTimeout);
				observableResults.Add(process.ExitCode);
			}));

			// Start EventBasedObservableProcess instances
			tasks.AddRange(Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var process = new EventBasedObservableProcess(TestCaseArguments(SingleLineTestCase));
				process.Subscribe(_ => { });
				process.WaitForCompletion(ParallelTimeout);
				eventBasedResults.Add(process.ExitCode);
			}));

			// Start Proc.Start instances
			tasks.AddRange(Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var args = TestCaseArguments(SingleLineTestCase);
				args.Timeout = ParallelTimeout;
				args.ConsoleOutWriter = new NoopConsoleOutWriter();
				var result = Proc.Start(args);
				procStartResults.Add(result.ExitCode);
			}));

			await Task.WhenAll(tasks);

			observableResults.Should().HaveCount(ParallelCount).And.OnlyContain(x => x == 0);
			eventBasedResults.Should().HaveCount(ParallelCount).And.OnlyContain(x => x == 0);
			procStartResults.Should().HaveCount(ParallelCount).And.OnlyContain(x => x == 0);
		}

		[Fact]
		public async Task ObservableProcess_WithOutput_CanRunInParallel()
		{
			// SlowOutput takes ~5s (10 lines * 500ms delay). With 5 parallel processes, allow extra time.
			var slowOutputTimeout = TimeSpan.FromSeconds(30);
			var results = new ConcurrentBag<(int index, List<string> lines)>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var seen = new List<string>();
				var args = TestCaseArguments(SlowOutputTestCase);
				args.Timeout = slowOutputTimeout;
				var process = new ObservableProcess(args);
				process.SubscribeLines(c => seen.Add(c.Line));
				process.WaitForCompletion(slowOutputTimeout);
				results.Add((i, seen));
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var (index, lines) in results)
			{
				// SlowOutput produces 10 lines: "x:1" through "x:10"
				lines.Should().HaveCount(10, $"process {index} should produce 10 lines");
				lines.Should().Contain("x:1");
				lines.Should().Contain("x:10");
			}
		}

		[Fact]
		public async Task EventBasedObservableProcess_WithOutput_CanRunInParallel()
		{
			// SlowOutput takes ~5s (10 lines * 500ms delay). With 5 parallel processes, allow extra time.
			var slowOutputTimeout = TimeSpan.FromSeconds(30);
			var results = new ConcurrentBag<(int index, List<string> lines)>();

			var tasks = Enumerable.Range(0, ParallelCount).Select(async i =>
			{
				await Task.Delay(i * 50); // Stagger starts to avoid thundering herd
				var seen = new List<string>();
				var args = TestCaseArguments(SlowOutputTestCase);
				args.Timeout = slowOutputTimeout;
				var process = new EventBasedObservableProcess(args);
				process.Subscribe(c => seen.Add(c.Line));
				process.WaitForCompletion(slowOutputTimeout);
				results.Add((i, seen));
			});

			await Task.WhenAll(tasks);

			results.Should().HaveCount(ParallelCount);
			foreach (var (index, lines) in results)
			{
				lines.Should().HaveCount(10, $"process {index} should produce 10 lines");
				lines.Should().Contain("x:1");
				lines.Should().Contain("x:10");
			}
		}

		private class NoopConsoleOutWriter : IConsoleOutWriter
		{
			public void Write(Exception e) { }
			public void Write(ConsoleOut consoleOut) { }
		}

		private class CollectingLineHandler : IConsoleLineHandler
		{
			private readonly List<string> _lines;

			public CollectingLineHandler(List<string> lines) => _lines = lines;

			public void Handle(LineOut lineOut) => _lines.Add(lineOut.Line);
			public void Handle(Exception e) => throw e;
		}
	}
}
