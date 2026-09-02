using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FluentAssertions;

namespace ProcNet.Tests
{
	public class KillOnParentExitTests : TestsBase
	{
		[SkipUnlessKillOnParentExitSupportedFact]
		public void KillOnParentExit_KillsGrandchildWhenMiddleProcessExits()
		{
			var pidFile = Path.Combine(Path.GetTempPath(), "procnet-kill-on-parent-exit-child.txt");
			if (File.Exists(pidFile)) File.Delete(pidFile);

			// KillOnParentExitChild starts a grandchild process with StartArguments.KillOnParentExit = true
			// and then exits itself; the grandchild should be terminated as a result.
			var args = ExecTestCaseArguments("KillOnParentExitChild");
			args.Timeout = TimeSpan.FromSeconds(15);
			Proc.Exec(args);

			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
				Thread.Sleep(50);

			File.Exists(pidFile).Should().BeTrue("the grandchild process should have written its PID before sleeping");
			var pid = int.Parse(File.ReadAllText(pidFile));

			// Give the OS a moment to register the kill.
			Thread.Sleep(500);

			Action check = () => Process.GetProcessById(pid);
			check.Should().Throw<ArgumentException>(
				"the grandchild should have been killed when its parent (the middle process) exited");
		}
	}
}
