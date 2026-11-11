using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TUnit.Core;

namespace ProcNet.Tests
{
	public sealed class SkipOnWindowsAttribute() : SkipAttribute("Skipped, this test can only run on non-windows platforms")
	{
		public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
			Task.FromResult(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
	}
}
