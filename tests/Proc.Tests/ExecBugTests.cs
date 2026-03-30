using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace ProcNet.Tests
{
	public class ExecBugTests : TestsBase
	{
		[Fact]
		public void Exec_OnTimeout_KillsChildProcess()
		{
			// Bug: when the timeout fires, ProcExecException is thrown but process.Kill() is
			// never called — HardWaitForExit is called on a still-running process, giving it
			// a 1s grace period that does nothing, and the child process keeps running.
			// After fix: process.Kill(entireProcessTree: true) is called before HardWaitForExit.
			var pidFile = Path.Combine(Path.GetTempPath(), "procnet-orphan-check.txt");
			if (File.Exists(pidFile)) File.Delete(pidFile);

			var args = ExecTestCaseArguments("WritePidAndWait"); // writes PID then sleeps 30s
			args.Timeout = TimeSpan.FromMilliseconds(500);

			Action call = () => Proc.Exec(args);
			call.Should().Throw<ProcExecException>().WithMessage("*Timeout*");

			// Give the OS a moment to register the kill
			Thread.Sleep(300);

			File.Exists(pidFile).Should().BeTrue("the child process should have written its PID before sleeping");
			var pid = int.Parse(File.ReadAllText(pidFile));

			Action check = () => Process.GetProcessById(pid);
			check.Should().Throw<ArgumentException>("the child process should be dead after a timeout");
		}
	}
}
