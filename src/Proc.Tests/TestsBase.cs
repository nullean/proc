using System;
using System.IO;
using System.Reflection;

namespace ProcNet.Tests
{
	public abstract class TestsBase
	{
		private static string _procTestBinary = "Proc.Tests.Binary";

		protected static TimeSpan WaitTimeout { get; } = TimeSpan.FromSeconds(5);
		protected static TimeSpan WaitTimeoutDebug { get; } = TimeSpan.FromMinutes(5);

		private static string GetWorkingDir()
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

		protected static StartArguments TestCaseArguments(string testcase) =>
			new StartArguments("dotnet", GetDll(), testcase)
			{
				WorkingDirectory = GetWorkingDir(),
			};

		private static string GetDll()
		{
			var dll = Path.Combine("bin", GetRunningConfiguration(), "netcoreapp1.1", _procTestBinary + ".dll");
			var fullPath = Path.Combine(GetWorkingDir(), dll);
			if (!File.Exists(fullPath)) throw new Exception($"Can not find {fullPath}");

			return dll;
		}

		private static string GetRunningConfiguration()
		{
			var l = typeof(TestsBase).GetTypeInfo().Assembly.Location;
			return new DirectoryInfo(l).Parent?.Parent?.Name;
		}
	}
}
