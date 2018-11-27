using System.Collections.Generic;
using System.Linq;

namespace ProcNet
{
	public class ExecArguments
	{

		public string Binary { get; }
		public IEnumerable<string> Args { get; }

		public ExecArguments(string binary, IEnumerable<string> args) : this(binary, args?.ToArray()) { }

		public ExecArguments(string binary, params string[] args)
		{
			this.Binary = binary;
			this.Args = args;
		}

		/// <summary>Provide environment variable scoped to the process being executed</summary>
		public IDictionary<string, string> Environment { get; set; }

		/// <summary> Set the current working directory</summary>
		public string WorkingDirectory { get; set; }

	}
}