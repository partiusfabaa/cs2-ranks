using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Ranks;

public static class Utils
{
    public static string ReplaceFirstCharacter(this string input)
    {
        if (input.Length <= 0) return input;

        var charArray = input.ToCharArray();
        charArray[6] = '1';

        return new string(charArray);
    }

    public static string GetTextInsideQuotes(string input)
    {
        var startIndex = input.IndexOf('"');
        var endIndex = input.LastIndexOf('"');

        if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
        {
            return input.Substring(startIndex + 1, endIndex - startIndex - 1);
        }

        return string.Empty;
    }

    public static CCSPlayerController? GetPlayer(string input)
    {
        var players = Utilities.GetPlayers();
        if (input.StartsWith("#"))
        {
            if (!int.TryParse(input.Trim('#'), out var value)) return null;
            
            return players.Find(p => p.Slot == value);
        }

        return players.FirstOrDefault(u => u.PlayerName.Contains(input));
    }
    
    public static int RoundToNearest(this float value)
    {
        return (int)Math.Round(value);
    }
}