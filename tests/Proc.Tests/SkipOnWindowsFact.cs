using System.Runtime.InteropServices;
using Xunit;

namespace ProcNet.Tests
{
	public sealed class SkipOnWindowsFact : FactAttribute
	{
		public SkipOnWindowsFact()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
			Skip = "Skipped, this test can only run on non-windows platforms";
		}
	}
}
