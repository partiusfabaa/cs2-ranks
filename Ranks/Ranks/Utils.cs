using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;

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
    
    public static bool GetPlayerBySteamId(string steamId, [NotNullWhen(true)] out CCSPlayerController? player)
    {
        player = null;

        if (steamId.Contains("STEAM_1"))
        {
            steamId = ReplaceFirstCharacter(steamId);
        }

        if (steamId.Contains("STEAM_") || steamId.Contains("765611"))
        {
            player = GetPlayerFromSteamId(steamId);
            return true;
        }

        return false;
    }
    
    public static CCSPlayerController? GetPlayerFromSteamId(string steamId)
    {
        return Utilities.GetPlayers().Find(u =>
            u.AuthorizedSteamID != null &&
            (u.AuthorizedSteamID.SteamId2.ToString().Equals(steamId) ||
             u.AuthorizedSteamID.SteamId64.ToString().Equals(steamId) ||
             u.AuthorizedSteamID.AccountId.ToString().Equals(steamId)));
    }
    
    public static CCSPlayerController? GetPlayer(string input)
    {
        var players = Utilities.GetPlayers();
        if (input.StartsWith('#'))
        {
            if (!int.TryParse(input.Trim('#'), out var value)) return null;
            
            return players.Find(p => p.Slot == value);
        }

        if (input.StartsWith('@'))
        {
            if (!GetPlayerBySteamId(input.Trim('@'), out var player)) return null;

            return player;
        }

        return players.FirstOrDefault(u => u.PlayerName.Contains(input));
    }
    
    public static int RoundToNearest(this float value)
    {
        return (int)Math.Round(value);
    }
}