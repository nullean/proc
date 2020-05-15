namespace ProcNet.Std
{
	public class CharactersOut : ConsoleOut
	{
		public override bool Error { get; }
		public char[] Characters { get; }
		internal CharactersOut(bool error, char[] characters)
		{
			Error = error;
			Characters = characters;
		}

		internal bool EndsWithNewLine =>
			Characters.Length > 0 && Characters[Characters.Length - 1] == '\n';

		internal bool StartsWithCarriage =>
			Characters.Length > 0 && Characters[0] == '\r';
	}
}
