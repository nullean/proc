using System;
using System.Runtime.InteropServices;
using Xunit;

namespace ProcNet.Tests
{
	public sealed class SkipOnNonWindowsFact : FactAttribute
	{
		public SkipOnNonWindowsFact()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
			Skip = "Skipped, this test can only run on windows";
		}
	}
}
