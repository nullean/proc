using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ProcNet.Tests
{
	public abstract class TestsBase
	{
		private static string _procTestBinary = "Proc.Tests.Binary";

		protected static TimeSpan WaitTimeout { get; } = TimeSpan.FromSeconds(5);
		protected static TimeSpan WaitTimeoutDebug { get; } = TimeSpan.FromMinutes(5);

		protected static string GetWorkingDir()
		{
			var directoryInfo = new DirectoryInfo(Directory.GetCurrentDirectory());

			var root = (directoryInfo.Name == "Proc.Tests"
			            && directoryInfo.Parent != null
			            && directoryInfo.Parent.Name == "src")
				? "./.."
				: @"../../../..";

			var binaryFolder = Path.Combine(Path.GetFullPath(root), _procTestBinary);
			return binaryFolder;
		}

		// Test cases run against a NativeAOT-published executable rather than `dotnet <dll>`, to avoid
		// dotnet-host/JIT startup overhead and variance in tests that depend on process lifecycle timing.
		// See Proc.Tests.Binary.csproj's PublishNativeAotAfterBuild target, which republishes it on every build.
		protected static StartArguments CmdTestCaseArguments(string testcase, params string[] args) {
			string[] arguments = ["/C", GetTestBinaryPath(), testcase];

			return new StartArguments("cmd", arguments.Concat(args)) {
				WorkingDirectory = GetWorkingDir(),
				Timeout = WaitTimeout
			};
		}

		protected static StartArguments TestCaseArguments(string testcase, params string[] args)
		{
			string[] arguments = [testcase];

			return new StartArguments(GetTestBinaryPath(), arguments.Concat(args))
			{
				WorkingDirectory = GetWorkingDir(),
				Timeout = WaitTimeout
			};
		}

		protected static ExecArguments ExecTestCaseArguments(string testcase, params string[] args)
		{
			string[] arguments = [testcase];
			return new ExecArguments(GetTestBinaryPath(), arguments.Concat(args))
			{
				WorkingDirectory = GetWorkingDir()
			};
		}

		protected static LongRunningArguments LongRunningTestCaseArguments(string testcase) =>
			new(GetTestBinaryPath(), testcase)
			{
				WorkingDirectory = GetWorkingDir(),
				Timeout = WaitTimeout
			};

		protected static string GetTestBinaryPath()
		{
#if NET11_0_OR_GREATER
			const string tfm = "net11.0";
#else
			const string tfm = "net10.0";
#endif
			var rid = RuntimeInformation.RuntimeIdentifier;
			var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? _procTestBinary + ".exe" : _procTestBinary;
			var exe = Path.Combine("bin", GetRunningConfiguration(), tfm, rid, "publish", exeName);
			var fullPath = Path.Combine(GetWorkingDir(), exe);
			if (!File.Exists(fullPath)) throw new Exception($"Can not find {fullPath}");

			// Unlike `dotnet <relative-dll-path>` (where dotnet itself resolves the dll relative to its own,
			// already-started, working directory), the executable path passed to Process.Start is resolved
			// relative to *this* process's cwd, not the child's ProcessStartInfo.WorkingDirectory. So this must
			// be absolute.
			return fullPath;
		}

		private static string GetRunningConfiguration()
		{
			var l = typeof(TestsBase).GetTypeInfo().Assembly.Location;
			return new DirectoryInfo(l).Parent?.Parent?.Name;
		}
	}
}
