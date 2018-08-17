using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Proc.Tests.Binary
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("no testcase specified");
				return 1;
			}

			var testCase = args[0].ToLowerInvariant();

			if (testCase == nameof(SingleLineNoEnter).ToLowerInvariant()) return SingleLineNoEnter();
			if (testCase == nameof(TwoWrites).ToLowerInvariant()) return TwoWrites();

			if (testCase == nameof(SingleLine).ToLowerInvariant()) return SingleLine();

			if (testCase == nameof(DelayedWriter).ToLowerInvariant()) return DelayedWriter();

			if (testCase == nameof(ReadKeyFirst).ToLowerInvariant()) return ReadKeyFirst();
			if (testCase == nameof(ReadKeyAfter).ToLowerInvariant()) return ReadKeyAfter();
			if (testCase == nameof(SlowOutput).ToLowerInvariant()) return SlowOutput();
			if (testCase == nameof(ReadLineFirst).ToLowerInvariant()) return ReadLineFirst();
			if (testCase == nameof(ReadLineAfter).ToLowerInvariant()) return ReadLineAfter();
			if (testCase == nameof(MoreText).ToLowerInvariant()) return MoreText();
			if (testCase == nameof(TrailingLines).ToLowerInvariant()) return TrailingLines();
			if (testCase == nameof(ControlC).ToLowerInvariant()) return ControlC();
			if (testCase == nameof(ControlCNoWait).ToLowerInvariant()) return ControlCNoWait();
			if (testCase == nameof(OverwriteLines).ToLowerInvariant()) return OverwriteLines();
			if (testCase == nameof(InterMixedOutAndError).ToLowerInvariant()) return InterMixedOutAndError();

			return 1;
		}
		private static int DelayedWriter()
		{
			Thread.Sleep(3000);
			Console.Write(nameof(DelayedWriter));
			return 20;
		}
		private static int SingleLineNoEnter()
		{
			Console.Write(nameof(SingleLineNoEnter));
			return 0;
		}
		private static int TwoWrites()
		{
			Console.Write(nameof(TwoWrites));
			Console.Write(nameof(TwoWrites));
			return 0;
		}
		private static int InterMixedOutAndError()
		{
			for (var i = 0; i < 200; i += 2)
			{
                Console.Out.WriteLine(new string(i.ToString()[0], 10));
                Console.Error.WriteLine(new string((i + 1).ToString()[0], 10));
				Task.Delay(1).Wait();
			}
			return 0;
		}
		private static int SingleLine()
		{
			Console.WriteLine(nameof(SingleLine));
			return 0;
		}
		private static int OverwriteLines()
		{
			for (var i = 0; i < 10; i++)
			{

				Console.Write($"\r{nameof(OverwriteLines)} {i}");
				Thread.Sleep(100);
			}
			return 102;
		}
		private static int ReadKeyFirst()
		{
			var read = Convert.ToChar(Console.Read());
			Console.Write($"{read}{nameof(ReadKeyFirst)}");
			return 21;
		}
		private static int ReadKeyAfter()
		{
			Console.Write("input:");
			Console.Read();
			Console.Write(nameof(ReadKeyAfter));
			return 21;
		}
		private static int SlowOutput()
		{
			for (var i = 1; i <= 10; i++)
			{
				Console.WriteLine("x:" + i);
				Thread.Sleep(500);
			}
			return 121;
		}
		private static int ReadLineFirst()
		{
			Console.ReadLine();
			Console.Write(nameof(ReadLineFirst));
			return 21;
		}
		private static int ReadLineAfter()
		{
			Console.Write("input:");
			Console.ReadLine();
			Console.Write(nameof(ReadLineAfter));
			return 21;
		}
		private static int ControlC()
		{
			Console.WriteLine("Written before control+c");
			Console.CancelKeyPress += (sender, args) =>
			{
				Console.WriteLine("Written after control+c");
			};
			Console.ReadLine();
			return 70;
		}
		private static int ControlCNoWait()
		{
			Console.WriteLine("Written before control+c");
			Console.CancelKeyPress += (sender, args) =>
			{
				Console.WriteLine("Written after control+c");
			};
			return 71;
		}

		private static int TrailingLines()
		{
			var output = @"Windows IP Configuration


";
			Console.Write(output);

			return 60;
		}

		private static int MoreText()
		{
			var output = @"
Windows IP Configuration


Ethernet adapter Ethernet 2:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Wireless LAN adapter Wi-Fi:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Ethernet adapter Ethernet 3:

   Connection-specific DNS Suffix  . :
   Link-local IPv6 Address . . . . . : fe80::e477:59c0:1f38:6cfa%16
   IPv4 Address. . . . . . . . . . . : 10.2.20.225
   Subnet Mask . . . . . . . . . . . : 255.255.255.0
   Default Gateway . . . . . . . . . : 10.2.20.1

Wireless LAN adapter Local Area Connection* 3:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Wireless LAN adapter Local Area Connection* 4:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Ethernet adapter Bluetooth Network Connection:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :
";
			Console.Write(output);

			return 40;
		}
	}
}
