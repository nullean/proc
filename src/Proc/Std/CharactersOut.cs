namespace ProcNet.Std
{
	public class CharactersOut : ConsoleOut
	{
		public override bool Error { get; }
		public char[] Characters { get; }
		internal CharactersOut(bool error, char[] characters)
		{
			this.Error = error;
			this.Characters = characters;
		}

		internal bool EndsWithNewLine =>
			this.Characters.Length > 0 && this.Characters[this.Characters.Length - 1] == '\n';

		internal bool StartsWithCarriage =>
			this.Characters.Length > 0 && this.Characters[0] == '\r';
	}
}
