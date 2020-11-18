using System.Collections.Generic;
using System.Linq;

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
	}
}