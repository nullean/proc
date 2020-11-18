using System;

namespace ProcNet.Std
{
	public abstract class ConsoleOut
	{
		public abstract bool Error { get; }

		public void OutOrErrrorCharacters(Action<char[]> outChararchters, Action<char[]>  errorCharacters)
		{
			switch (this)
			{
				case CharactersOut c:
                    if (Error) errorCharacters(c.Characters);
                    else outChararchters(c.Characters);
					break;
				default:
					throw new Exception($"{nameof(ConsoleOut)} is not of type {nameof(CharactersOut)} {nameof(OutOrErrrorCharacters)} not allowed");
			}
		}
		public void CharsOrString(Action<char[]> doCharacters, Action<string> doLine)
		{
			switch (this)
			{
				case CharactersOut c:
					doCharacters(c.Characters);
					break;
				case LineOut l:
					doLine(l.Line);
					break;
			}
		}


		public static LineOut ErrorOut(string data) => new LineOut(true, data);
		public static LineOut Out(string data) => new LineOut(false, data);

		public static CharactersOut ErrorOut(char[] characters) => new CharactersOut(true, characters);
		public static CharactersOut Out(char[] characters) => new CharactersOut(false, characters);
	}
}
