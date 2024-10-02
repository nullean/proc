using System.Collections.Generic;
using System.Linq;

namespace ProcNet.Extensions;

internal static class ArgumentExtensions
{
	public static string NaivelyQuoteArguments(this IEnumerable<string> arguments)
	{
		if (arguments == null) return string.Empty;
		var args = arguments.ToList();
		if (args.Count == 0) return string.Empty;
		var quotedArgs = args
			.Select(a =>
			{
				if (!a.Contains(" ")) return a;
				return $"\"{a}\"";
			})
			.ToList();
		return string.Join(" ", quotedArgs);
	}

}
