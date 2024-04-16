using System.Text.Json;
using CounterStrikeSharp.API.Core;
using RanksApi;

namespace Ranks;

public class RanksApi : IRanksApi
{
    public event Action<CCSPlayerController, int, bool>? RankChanged;
    public event Func<CCSPlayerController, int, int?>? PlayerGainedExperience;
    public event Func<CCSPlayerController, int, int?>? PlayerLostExperience;
    
    public string CoreConfigDirectory { get; }
    public string ModulesConfigDirectory => Path.Combine(CoreConfigDirectory, "Modules/");
    public string DatabaseTableName => _ranks.Config.TableName;
    public string DatabaseConnectionString => _ranks.DbConnectionString;
    public bool IsRanksEnabled => _ranks.IsRanksEnabled;

    private readonly Ranks _ranks;

    public RanksApi(Ranks ranks, string directory)
    {
        _ranks = ranks;
        CoreConfigDirectory = new DirectoryInfo(directory).Parent?.Parent?.FullName + "/configs/plugins/RanksCore/";
    }
    
    public int GetPlayerExperience(CCSPlayerController player)
    {
        return _ranks.Users.TryGetValue(player.SteamID, out var user) ? user.value : -1;
    }

    public int GetPlayerRank(CCSPlayerController player)
    {
        return _ranks.Users.TryGetValue(player.SteamID, out var user) ? user.rank : -1;
    }

    public (string Name, int Level) GetLevelFromExperience(long experience)
    {
        return _ranks.GetLevelFromExperience(experience);
    }

    public void SetPlayerExperience(CCSPlayerController player, int exp)
    {
        if (!_ranks.Users.TryGetValue(player.SteamID, out var user)) return;

        user.value = exp;
    }

    public void GivePlayerExperience(CCSPlayerController player, int exp)
    {
        if (!_ranks.Users.TryGetValue(player.SteamID, out var user)) return;

        user.value += exp;
    }
    
    public void TakePlayerExperience(CCSPlayerController player, int exp)
    {
        if (!_ranks.Users.TryGetValue(player.SteamID, out var user)) return;

        user.value -= exp;
    }

    public void PrintToChat(CCSPlayerController player, string message)
    {
        _ranks.PrintToChat(player, message);
    }

    public void PrintToChatAll(string message)
    {
        _ranks.PrintToChatAll(message);
    }
    
    public T LoadConfig<T>(string name, string path)
    {
        var configFilePath = Path.Combine(path, $"{name}.json");

        if (!File.Exists(configFilePath))
        {
            var defaultConfig = Activator.CreateInstance<T>();
            var defaultJson =
                JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, defaultJson);
        }

        var configJson = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<T>(configJson);

        if (config == null)
            throw new FileNotFoundException($"File {name}.json not found or cannot be deserialized");

        return config;
    }

    public T LoadConfig<T>(string name)
    {
        return LoadConfig<T>(name, ModulesConfigDirectory);
    }

    public void OnRankChanged(CCSPlayerController player, int newRank, bool promoted)
    {
        RankChanged?.Invoke(player, newRank, promoted);
    }
    
    public int? OnPlayerGainedExperience(CCSPlayerController player, int exp)
    {
        return PlayerGainedExperience?.Invoke(player, exp);
    }
    
    public int? OnPlayerLostExperience(CCSPlayerController player, int exp)
    {
        return PlayerLostExperience?.Invoke(player, exp);
    }
}