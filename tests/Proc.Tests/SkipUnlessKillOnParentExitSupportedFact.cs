using System;
using Xunit;

namespace ProcNet.Tests
{
	/// <summary>
	/// <see cref="StartArguments.KillOnParentExit"/> is only wired up on .NET 11+, and even there the underlying
	/// runtime feature only supports Windows and Linux (not macOS) as of .NET 11 Preview 7.
	/// </summary>
	public sealed class SkipUnlessKillOnParentExitSupportedFact : FactAttribute
	{
		public SkipUnlessKillOnParentExitSupportedFact()
		{
#if NET11_0_OR_GREATER
			if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) return;
#endif
			Skip = "Skipped, KillOnParentExit requires .NET 11+ on Windows or Linux";
		}
	}
}
