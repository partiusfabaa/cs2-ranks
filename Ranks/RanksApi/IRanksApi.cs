using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Menu;

namespace RanksApi;

public interface IRanksApi
{
    public static PluginCapability<IRanksApi> Capability { get; } = new("ranks-core:api");

    event Action<CCSPlayerController, int, bool>? RankChanged;
    event Func<CCSPlayerController, int, int?>? PlayerExperienceChanged;

    [Obsolete("Use PlayerExperienceChanged instead.")]
    event Func<CCSPlayerController, int, int?>? PlayerGainedExperience;

    [Obsolete("Use PlayerExperienceChanged instead.")]
    event Func<CCSPlayerController, int, int?>? PlayerLostExperience;

    event Action<CCSPlayerController, IMenu>? CreatedMenu; 

    string CoreConfigDirectory { get; }
    string ModulesConfigDirectory { get; }
    string DatabaseConnectionString { get; }
    string DatabaseTableName { get; }
    bool IsRanksEnabled { get; }
    int GetPlayerExperience(CCSPlayerController player);
    int GetPlayerRank(CCSPlayerController player);
    (string Name, int Level) GetLevelFromExperience(long experience);
    void SetPlayerExperience(CCSPlayerController player, int exp);
    void TakePlayerExperience(CCSPlayerController player, int exp);
    void GivePlayerExperience(CCSPlayerController player, int exp);
    void PrintToChat(CCSPlayerController player, string message);
    void PrintToChatAll(string message);
    T LoadConfig<T>(string name, string path);
    T LoadConfig<T>(string name);
}