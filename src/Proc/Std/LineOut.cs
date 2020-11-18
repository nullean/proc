using System;

namespace ProcNet.Std
{
	/// <summary>
	/// This class represents a single line that was printed on the console.
	/// </summary>
	public class LineOut : ConsoleOut
	{
		private static readonly char[] NewlineChars = Environment.NewLine.ToCharArray();

		/// <summary>
		/// Indicates if this messages originated from standard error output.
		/// </summary>
		public override bool Error { get; }

		public string Line { get; }

		internal LineOut(bool error, string line)
		{
			Error = error;
			Line = line;
		}

		/// <summary>
		/// Converts a <see cref="CharactersOut"/> instance to an <see cref="LineOut"/> instance.
		/// If <paramref name="consoleOut"/> is of type <see cref="LineOut"/> already we simply return that instance
		/// otherwise this will return null.
		/// </summary>
		public static LineOut From(ConsoleOut consoleOut)
		{
			switch (consoleOut)
			{
				default: return null;
				case LineOut l: return l;
				case CharactersOut c:
					var s = new string(c.Characters, 0, c.Characters.Length).TrimEnd(NewlineChars);
					return consoleOut.Error ? ErrorOut(s) : Out(s);
			}
		}
	}
}
