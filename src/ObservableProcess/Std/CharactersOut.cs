namespace Elastic.ProcessManagement.Std
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
	}
}
