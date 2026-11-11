using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TUnit.Core;

namespace ProcNet.Tests;

public sealed class SkipOnNonWindowsAttribute() : SkipAttribute("Skipped, this test can only run on windows")
{
	public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
}
