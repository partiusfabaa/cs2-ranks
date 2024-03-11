using CounterStrikeSharp.API.Core;

namespace RanksApi;

public interface IRanksApi
{
    event Action<CCSPlayerController, int, bool>? RankChanged;
    event Func<CCSPlayerController, int, int?>? PlayerGainedExperience;
    event Func<CCSPlayerController, int, int?>? PlayerLostExperience;
    string CoreConfigDirectory { get; }
    string ModulesConfigDirectory { get; }
    string DatabaseConnectionString { get; }
    string DatabaseTableName { get; }
    bool IsRanksEnabled { get; }
    int GetPlayerExperience(CCSPlayerController player);
    int GetPlayerRank(CCSPlayerController player);
    void SetPlayerExperience(CCSPlayerController player, int exp);
    void TakePlayerExperience(CCSPlayerController player, int exp);
    void GivePlayerExperience(CCSPlayerController player, int exp);
    void PrintToChat(CCSPlayerController player, string message);
    void PrintToChatAll(string message);
    T LoadConfig<T>(string name, string path);
    T LoadConfig<T>(string name);
}
