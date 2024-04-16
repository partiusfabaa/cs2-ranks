using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using MySqlConnector;
using RanksApi;

namespace Ranks_ExStats_Hits;

public class RanksExStatsHits : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "[Ranks] ExStats Hits";
    public override string ModuleVersion => "v1.0.1";

    public readonly Dictionary<int, Dictionary<HitData, int>> Hits = new();
    private IRanksApi? _api;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = IRanksApi.Capability.Get();
        if (_api == null) return;

        Task.Run(CreateTable);

        RegisterEventHandler<EventPlayerHurt>(EventPlayerHurt);

        RegisterListener<Listeners.OnClientAuthorized>(OnAuthorized);
        RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
    }

    private void OnAuthorized(int slot, SteamID id)
    {
        Task.Run(() => LoadHitsFromDatabase(slot, id));
    }

    private void OnDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player.IsBot) return;
        var steamId =
            new SteamID(player.AuthorizedSteamID == null ? player.SteamID : player.AuthorizedSteamID.SteamId64);

        Task.Run(() => SavePlayerData(slot, steamId));
    }

    private HookResult EventPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var player = @event.Attacker;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        Hits[player.Slot][HitData.HdDmgHealth] += @event.DmgHealth;
        Hits[player.Slot][HitData.HdDmgArmor] += @event.DmgArmor;
        Hits[player.Slot][(HitData)@event.Hitgroup]++;

        return HookResult.Continue;
    }

    private async Task CreateTable()
    {
        try
        {
            await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
            await connection.OpenAsync();

            var query = $"""
                         CREATE TABLE IF NOT EXISTS `{_api.DatabaseTableName}_hits`
                         (`SteamID` varchar(32) NOT NULL PRIMARY KEY DEFAULT '',
                         	`DmgHealth` int NOT NULL DEFAULT 0,
                         	`DmgArmor` int NOT NULL DEFAULT 0,
                         	`Head` int NOT NULL DEFAULT 0,
                         	`Chest` int NOT NULL DEFAULT 0,
                         	`Belly` int NOT NULL DEFAULT 0,
                         	`LeftArm` int NOT NULL DEFAULT 0,
                         	`RightArm` int NOT NULL DEFAULT 0,
                         	`LeftLeg` int NOT NULL DEFAULT 0,
                         	`RightLeg` int NOT NULL DEFAULT 0,
                         	`Neak` int NOT NULL DEFAULT 0) CHARSET = utf8 COLLATE utf8_general_ci;
                         """;

            await connection.ExecuteAsync(query);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task AddPlayerToDatabase(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
            await connection.OpenAsync();

            var query = $"INSERT INTO `{_api.DatabaseTableName}_hits` (`SteamID`) VALUES (@SteamId);";

            await connection.ExecuteAsync(query, new { SteamId = steamId });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task SavePlayerData(int slot, SteamID steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
            await connection.OpenAsync();

            var updateQuery = $@"
                UPDATE `{_api.DatabaseTableName}_hits`
                SET DmgHealth = @DmgHealth,
                    DmgArmor = @DmgArmor,
                    Head = @Head,
                    Chest = @Chest,
                    Belly = @Belly,
                    LeftArm = @LeftArm,
                    RightArm = @RightArm,
                    LeftLeg = @LeftLeg,
                    RightLeg = @RightLeg,
                    Neak = @Neak
                WHERE SteamID = @SteamId;";

            await connection.ExecuteAsync(updateQuery, new
            {
                DmgHealth = Hits[slot][HitData.HdDmgHealth],
                DmgArmor = Hits[slot][HitData.HdDmgArmor],
                Head = Hits[slot][HitData.HdHitHead],
                Chest = Hits[slot][HitData.HdHitChest],
                Belly = Hits[slot][HitData.HdHitBelly],
                LeftArm = Hits[slot][HitData.HdHitLeftArm],
                RightArm = Hits[slot][HitData.HdHitRightArm],
                LeftLeg = Hits[slot][HitData.HdHitLeftLeg],
                RightLeg = Hits[slot][HitData.HdHitRightLeg],
                Neak = Hits[slot][HitData.HdHitNeak],
                SteamId = ReplaceFirstCharacter(steamId.SteamId2)
            });

            Hits.Remove(slot);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task LoadHitsFromDatabase(int slot, SteamID id)
    {
        try
        {
            await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
            await connection.OpenAsync();

            var steamId = ReplaceFirstCharacter(id.SteamId2);

            var query = $"SELECT * FROM `{_api.DatabaseTableName}_hits` WHERE SteamId = @SteamId;";
            var hitsData = await connection.QueryFirstOrDefaultAsync(query, new { SteamId = steamId });

            Hits.Add(slot, Enum.GetValues(typeof(HitData)).Cast<HitData>().ToDictionary(hit => hit, hit => 0));
            
            if (hitsData == null)
            {
                await AddPlayerToDatabase(steamId);
                return;
            }

            Hits[slot] = new Dictionary<HitData, int>
            {
                [HitData.HdDmgHealth] = hitsData.DmgHealth,
                [HitData.HdDmgArmor] = hitsData.DmgArmor,
                [HitData.HdHitHead] = hitsData.Head,
                [HitData.HdHitChest] = hitsData.Chest,
                [HitData.HdHitBelly] = hitsData.Belly,
                [HitData.HdHitLeftArm] = hitsData.LeftArm,
                [HitData.HdHitRightArm] = hitsData.RightArm,
                [HitData.HdHitLeftLeg] = hitsData.LeftLeg,
                [HitData.HdHitRightLeg] = hitsData.RightLeg,
                [HitData.HdHitNeak] = hitsData.Neak
            };
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static string ReplaceFirstCharacter(string input)
    {
        if (input.Length <= 0) return input;

        var charArray = input.ToCharArray();
        charArray[6] = '1';

        return new string(charArray);
    }
}