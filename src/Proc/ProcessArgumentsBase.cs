using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ProcNet
{
	public abstract class ProcessArgumentsBase
	{
		public ProcessArgumentsBase(string binary, IEnumerable<string> args) : this(binary, args?.ToArray()) { }

		public ProcessArgumentsBase(string binary, params string[] args)
		{
			Binary = binary;
			Args = args;
		}

		public string Binary { get; }
		public IEnumerable<string> Args { get; }

		/// <summary>Provide environment variable scoped to the process being executed</summary>
		public IDictionary<string, string> Environment { get; set; }

		/// <summary> Set the current working directory</summary>
		public string WorkingDirectory { get; set; }

		/// <summary> Force arguments and the current working director NOT to be part of the exception message </summary>
		public bool OnlyPrintBinaryInExceptionMessage { get; set; }

		/// <summary>
		/// Ensures the started process is terminated when this process exits, including forced terminations and
		/// crashes. Backed by Job objects on Windows and <c>PR_SET_PDEATHSIG</c> on Linux/Android.
		/// <para>
		/// Only takes effect when running on .NET 11 or greater on Windows or Linux; it is a no-op everywhere else
		/// (including macOS, which the underlying runtime feature does not yet support).
		/// </para>
		/// </summary>
		public bool KillOnParentExit { get; set; }

		/// <summary>
		/// Restricts which handles the started process inherits, instead of the default behaviour of inheriting
		/// every inheritable handle from this process. An empty list means only the standard handles are inherited.
		/// <para>Only takes effect when running on .NET 11 or greater; it is a no-op on older target frameworks.</para>
		/// <para>Only <see cref="Microsoft.Win32.SafeHandles.SafeFileHandle"/> and <see cref="Microsoft.Win32.SafeHandles.SafePipeHandle"/> are supported by the underlying runtime feature.</para>
		/// </summary>
		public IList<SafeHandle> InheritedHandles { get; set; }

	}
}
