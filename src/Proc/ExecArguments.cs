using System;
using System.Collections.Generic;

namespace ProcNet
{
	public class ExecArguments : ProcessArgumentsBase
	{
		private Func<int, bool> _validExitCodeClassifier;
		public ExecArguments(string binary, IEnumerable<string> args) : base(binary, args) { }

		public ExecArguments(string binary, params string[] args) : base(binary, args) { }

		public Func<int, bool> ValidExitCodeClassifier
		{
			get => _validExitCodeClassifier ?? (c => c == 0);
			set => _validExitCodeClassifier = value;
		}

		public TimeSpan? Timeout { get; set; }
	}
}
