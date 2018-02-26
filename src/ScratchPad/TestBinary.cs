using System;
using System.IO;
using System.Reflection;
using ProcNet;

namespace ScratchPad
{
	public static class TestBinary
	{
		private static string _procTestBinary = "Proc.Tests.Binary";

		//increase this if you are using the debugger
		private static TimeSpan WaitTimeout { get; } = TimeSpan.FromSeconds(5);

		private static string GetWorkingDir()
		{
			var directoryInfo = new DirectoryInfo(Directory.GetCurrentDirectory());

			var root = (directoryInfo.Name == "ScratchPad"
			            && directoryInfo.Parent != null
			            && directoryInfo.Parent.Name == "src")
				? "./.."
				: @"../../../..";

			var binaryFolder = Path.Combine(Path.GetFullPath(root), _procTestBinary);
			return binaryFolder;
		}

		public static StartArguments TestCaseArguments(string testcase) =>
			new StartArguments("dotnet", GetDll(), testcase)
			{
				WorkingDirectory = GetWorkingDir()
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
			var l = typeof(TestBinary).GetTypeInfo().Assembly.Location;
			return new DirectoryInfo(l).Parent?.Parent?.Name;
		}
	}
}
