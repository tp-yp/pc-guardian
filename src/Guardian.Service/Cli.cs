using System.Text;

namespace Guardian.Service;

internal static class Cli
{
    public static string ReadPassword(string prompt)
    {
        Console.Write(prompt + ": ");
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    public static string ReadNewPassword(string prompt)
    {
        while (true)
        {
            var a = ReadPassword(prompt);
            if (a.Length < 8) { Console.WriteLine("At least 8 characters."); continue; }
            if (a == ReadPassword("Repeat")) return a;
            Console.WriteLine("Passwords do not match.");
        }
    }
}
