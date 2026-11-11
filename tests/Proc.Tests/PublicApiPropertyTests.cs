using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using AwesomeAssertions;
using ProcNet.Std;

namespace ProcNet.Tests;

/// <summary>
/// CsCheck property tests over Proc's public API: arguments, environment variables and exit codes must survive the
/// round trip through a real child process, for every value the generators come up with.
/// <para>
/// On net11.0, <see cref="Proc.Exec(ExecArguments)"/> and <see cref="Proc.ExecAsync"/> call
/// <see cref="Process.Run(ProcessStartInfo, TimeSpan?)"/>/<see cref="Process.RunAsync(ProcessStartInfo, CancellationToken)"/>
/// directly when no timeout (and no cancellable token) is given, and fall back to Proc's own machinery otherwise.
/// The differential properties below assert both paths agree with each other, and with the runtime API itself.
/// </para>
/// </summary>
public class PublicApiPropertyTests : TestsBase
{
	// Each iteration spawns a real process, so keep the iteration count modest; CsCheck still shrinks to a minimal
	// counterexample on failure.
	private const int ProcessIterations = 25;

	// Exit codes are truncated to 8 bits on Unix.
	private static readonly Gen<int> ExitCodes = Gen.Int[0, 255];

	// Printable ASCII, deliberately including spaces, quotes and backslashes, which are what argument quoting gets
	// wrong. Newlines are excluded because the child prints one argument per line.
	private static readonly Gen<string> Argument = Gen.String[Gen.Char[' ', '~'], 1, 24];

	private static readonly Gen<string[]> Arguments = Argument.Array[0, 8];

	private static readonly Gen<(string Name, string Value)[]> EnvironmentVariables = Gen
		.Select(Gen.String[Gen.Char['A', 'Z'], 1, 12], Argument)
		.Array[1, 6]
		.Select(vars => vars.GroupBy(v => v.Item1).Select(g => ("PROC_CSCHECK_" + g.Key, g.First().Item2)).ToArray());

	[Test]
	public void StartRoundTripsArguments() =>
		Arguments.Sample(
			args =>
			{
				var result = Proc.Start(TestCaseArguments("PrintArgs", args));

				result.Completed.Should().BeTrue();
				result.ExitCode.Should().Be(0);
				result.ConsoleOut.Select(l => l.Line).Should().Equal(args);
				result.ConsoleOut.Should().NotContain(l => l.Error);
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void StartPassesEnvironmentVariables() =>
		EnvironmentVariables.Sample(
			vars =>
			{
				var args = TestCaseArguments("PrintEnv", vars.Select(v => v.Name).ToArray());
				args.Environment = vars.ToDictionary(v => v.Name, v => v.Value);

				var result = Proc.Start(args);

				result.ExitCode.Should().Be(0);
				result.ConsoleOut.Select(l => l.Line).Should().Equal(vars.Select(v => v.Value));
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void NullEnvironmentValueRemovesAnInheritedVariable()
	{
		const string name = "PROC_TESTS_INHERITED";
		Environment.SetEnvironmentVariable(name, "from-parent");
		try
		{
			var inherited = Proc.Start(TestCaseArguments("PrintEnv", name));
			inherited.ConsoleOut.Select(l => l.Line).Should().Equal("from-parent");

			var args = TestCaseArguments("PrintEnv", name);
			args.Environment = new Dictionary<string, string> { [name] = null };
			var removed = Proc.Start(args);
			removed.ConsoleOut.Select(l => l.Line).Should().Equal("<unset>");
		}
		finally
		{
			Environment.SetEnvironmentVariable(name, null);
		}
	}

	[Test]
	public void StartReportsExitCode() =>
		ExitCodes.Sample(
			code =>
			{
				var result = Proc.Start(TestCaseArguments("ExitWith", Code(code)));

				result.Completed.Should().BeTrue();
				result.ExitCode.Should().Be(code);
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void StartRedirectedReportsExitCode() =>
		ExitCodes.Sample(
			code =>
			{
				var result = Proc.StartRedirected(TestCaseArguments("ExitWith", Code(code)));

				result.Completed.Should().BeTrue();
				result.ExitCode.Should().Be(code);
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void ExecReturnsExitCodeTheClassifierAccepts() =>
		ExitCodes.Sample(
			code =>
			{
				var args = ExecTestCaseArguments("ExitWith", Code(code));
				args.ValidExitCodeClassifier = _ => true;

				Proc.Exec(args).Should().Be(code);
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void ExecThrowsWithExitCodeTheClassifierRejects() =>
		ExitCodes.Sample(
			code =>
			{
				var args = ExecTestCaseArguments("ExitWith", Code(code));

				if (code == 0)
					Proc.Exec(args).Should().Be(0);
				else
					FluentActions
						.Invoking(() => Proc.Exec(args))
						.Should()
						.Throw<ProcExecException>()
						.Where(e => e.ExitCode == code)
						.WithMessage($"Process exited with '{code}'*");
			},
			iter: ProcessIterations,
			threads: 1
		);

	/// <summary> Without a timeout net11.0 goes through Process.Run, with one through Proc's own wait/kill loop. </summary>
	[Test]
	public void ExecAgreesWithAndWithoutTimeout() =>
		Gen.Select(ExitCodes, Arguments).Sample(
			(code, extra) =>
			{
				string[] args = [Code(code), .. extra];
				var direct = ExecTestCaseArguments("ExitWith", args);
				direct.ValidExitCodeClassifier = _ => true;
				var timed = ExecTestCaseArguments("ExitWith", args);
				timed.ValidExitCodeClassifier = _ => true;
				timed.Timeout = TimeSpan.FromSeconds(30);

				Proc.Exec(direct).Should().Be(Proc.Exec(timed));
			},
			iter: ProcessIterations,
			threads: 1
		);

	/// <summary>
	/// A non-cancellable token takes the Process.RunAsync path on net11.0, a cancellable one Proc's own path. Both
	/// must report the same exit code and throw the same way.
	/// </summary>
	[Test]
	public Task ExecAsyncAgreesWithAndWithoutCancellableToken() =>
		ExitCodes.SampleAsync(
			async code =>
			{
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
				var uncancellable = await Capture(() => Proc.ExecAsync(ExecTestCaseArguments("ExitWith", Code(code))));
				var cancellable = await Capture(() => Proc.ExecAsync(ExecTestCaseArguments("ExitWith", Code(code)), cts.Token));

				uncancellable.Should().BeEquivalentTo(cancellable);
				uncancellable.ExitCode.Should().Be(code);
			},
			iter: ProcessIterations,
			threads: 1
		);

	[Test]
	public void ExecFailsTheSameWayWithAndWithoutTimeoutWhenTheBinaryDoesNotExist()
	{
		var direct = new ExecArguments("this-binary-does-not-exist-proc-cscheck");
		var timed = new ExecArguments("this-binary-does-not-exist-proc-cscheck") { Timeout = TimeSpan.FromSeconds(30) };

		var directError = FluentActions.Invoking(() => Proc.Exec(direct)).Should().Throw<Exception>().Which;
		var timedError = FluentActions.Invoking(() => Proc.Exec(timed)).Should().Throw<Exception>().Which;

		directError.GetType().Should().Be(timedError.GetType());
		directError.Message.Should().Be(timedError.Message);
	}

#if NET11_0_OR_GREATER
	/// <summary> Differential against the runtime: Proc.Exec must report exactly what Process.Run reports. </summary>
	[Test]
	public void ExecMatchesProcessRun() =>
		ExitCodes.Sample(code =>
		{
			var args = ExecTestCaseArguments("ExitWith", Code(code));
			args.ValidExitCodeClassifier = _ => true;
			var info = new ProcessStartInfo(GetTestBinaryPath(), ["ExitWith", Code(code)]) { WorkingDirectory = GetWorkingDir() };

			Proc.Exec(args).Should().Be(Process.Run(info).ExitCode);
		}, iter: ProcessIterations, threads: 1);

	/// <summary> Differential against the runtime: Proc.Start must capture exactly the stdout Process.RunAndCaptureText captures. </summary>
	[Test]
	public void StartMatchesProcessRunAndCaptureText() =>
		Arguments.Sample(args =>
		{
			var info = new ProcessStartInfo(GetTestBinaryPath(), ["PrintArgs", .. args])
			{
				WorkingDirectory = GetWorkingDir(),
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			var expected = Process.RunAndCaptureText(info, TimeSpan.FromSeconds(30));

			var result = Proc.Start(TestCaseArguments("PrintArgs", args));

			result.ExitCode.Should().Be(expected.ExitStatus.ExitCode);
			var expectedLines = expected.StandardOutput.Split(Environment.NewLine);
			// RunAndCaptureText keeps the final newline, which leaves one empty trailing element.
			result.ConsoleOut.Select(l => l.Line).Should().Equal(expectedLines.Take(expectedLines.Length - 1));
		}, iter: ProcessIterations, threads: 1);
#endif

	[Test]
	public void DefaultExitCodeClassifierOnlyAcceptsZero() =>
		Gen.Int.Sample(code =>
		{
			var args = new ExecArguments("irrelevant");
			args.ValidExitCodeClassifier(code).Should().Be(code == 0);

			args.ValidExitCodeClassifier = _ => false;
			args.ValidExitCodeClassifier = null;
			args.ValidExitCodeClassifier(code).Should().Be(code == 0, "setting null restores the default");
		});

	[Test]
	public void ArgumentsAreExposedAsGiven() =>
		Gen.Select(Argument, Arguments).Sample((binary, args) =>
		{
			new ExecArguments(binary, args).Args.Should().Equal(args);
			new StartArguments(binary, args.AsEnumerable()).Args.Should().Equal(args);
			new LongRunningArguments(binary, args).Binary.Should().Be(binary);
		});

	[Test]
	public void ProcessResultToStringReportsEveryLine() =>
		Gen.Select(Gen.Bool, Gen.Int.Nullable(), Gen.Select(Gen.Bool, Argument).Array[0, 10]).Sample((completed, exitCode, lines) =>
		{
			var consoleOut = lines.Select(l => l.Item1 ? ConsoleOut.ErrorOut(l.Item2) : ConsoleOut.Out(l.Item2)).ToList();
			var result = new ProcessCaptureResult(completed, consoleOut, exitCode);

			var text = result.ToString();
			text.Should().StartWith(new ProcessResult(completed, exitCode).ToString());
			var reported = text.Split(Environment.NewLine).Skip(2).Where(l => l.Length > 0).ToArray();
			reported.Should().Equal(lines.Select(l => $"[std{(l.Item1 ? "err" : "out")}]: {l.Item2}"));
		});

	[Test]
	public void ProcessCaptureResultNeverExposesNullConsoleOut() =>
		Gen.Select(Gen.Bool, Gen.Int.Nullable()).Sample((completed, exitCode) =>
		{
			var result = new ProcessCaptureResult(completed, null, exitCode);
			result.ConsoleOut.Should().NotBeNull().And.BeEmpty();
			result.Completed.Should().Be(completed);
			result.ExitCode.Should().Be(exitCode);
		});

	private static string Code(int code) => code.ToString(System.Globalization.CultureInfo.InvariantCulture);

	private record ExecOutcome(int? ExitCode, Type Error, int? ErrorExitCode, string ErrorMessage);

	private static async Task<ExecOutcome> Capture(Func<Task<int>> exec)
	{
		try
		{
			return new ExecOutcome(await exec(), null, null, null);
		}
		catch (ProcExecException e)
		{
			return new ExecOutcome(e.ExitCode, e.GetType(), e.ExitCode, e.Message);
		}
	}
}
