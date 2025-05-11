using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
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
        
        RegisterListener<Listeners.OnClientAuthorized>(OnAuthorized);
        RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
 
        RegisterEventHandler<EventPlayerHurt>(EventPlayerHurt);
        
        _api.CreatedMenu += OnCreatedMenu;
    }

    private void OnCreatedMenu(CCSPlayerController player, IMenu menu)
    {
        menu.AddMenuOption(Localizer["hits_statistics"], OpenSubMenu);
    }

    private void OpenSubMenu(CCSPlayerController player, ChatMenuOption option)
    {
        var menu = new ChatMenu(Localizer["hit_statistics"]);
        var subMenu = new ChatMenu("");
        subMenu.AddMenuOption("Back", OpenSubMenu);

        var hits = Hits[player.Slot];
        var allHits = hits[HitData.HdHitAll];
        
        if (allHits is not 0)
        {
            var hitTitle = Localizer["hits"];

            menu.AddMenuOption(hitTitle, (controller, _) =>
            {
                subMenu.Title = hitTitle;

                var head = hits[HitData.HdHitHead];
                var chest = hits[HitData.HdHitChest];
                var belly = hits[HitData.HdHitBelly];
                var leftArm = hits[HitData.HdHitLeftArm];
                var rightArm = hits[HitData.HdHitRightArm];
                var leftLeg = hits[HitData.HdHitLeftLeg];
                var rightLeg = hits[HitData.HdHitRightLeg];
                
                subMenu.AddMenuOption(Localizer["hits_player",
                    allHits, 
                    head,
                    chest,
                    belly,
                    leftArm,
                    rightArm,
                    leftLeg,
                    rightLeg].Value.Replace('\n', '\u2029'), null!, true);
                
                subMenu.Open(controller);
            });

            var damageTitle = Localizer["damage"];
            menu.AddMenuOption(damageTitle, (controller, _) =>
            {
                subMenu.Title = damageTitle;

                var health = hits[HitData.HdDmgHealth];
                var armor = hits[HitData.HdDmgArmor];

                allHits = health + armor;
            
                subMenu.AddMenuOption(Localizer["dmg_player",
                    health,
                    armor].Value.Replace('\n', '\u2029'), null!, true);

                subMenu.Open(controller);
            });
        }
        else
        {
            menu.AddMenuOption($"No data", null!, true);
        }

        menu.Open(player);
    }
    
    private void OnAuthorized(int slot, SteamID id)
    {
        Task.Run(() => LoadHitsFromDatabase(slot, id));
    }

    private void OnDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is null || player.IsBot) return;

        var steamId =
            new SteamID(player.AuthorizedSteamID == null ? player.SteamID : player.AuthorizedSteamID.SteamId64);

        Task.Run(() => SavePlayerData(slot, steamId));
    }

    private HookResult EventPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var player = @event.Attacker;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        if (!Hits.TryGetValue(player.Slot, out var hits))
            return HookResult.Continue;

        hits[HitData.HdDmgHealth] += @event.DmgHealth;
        hits[HitData.HdDmgArmor] += @event.DmgArmor;

        hits[HitData.HdHitAll]++;
        hits[(HitData)@event.Hitgroup]++;

        return HookResult.Continue;
    }

    private async Task CreateTable()
    {
        try
        {
            await using var connection = new MySqlConnection(_api?.DatabaseConnectionString);
            await connection.OpenAsync();

            var query = $"""
                         CREATE TABLE IF NOT EXISTS `{_api?.DatabaseTableName}_hits`
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
            await using var connection = new MySqlConnection(_api?.DatabaseConnectionString);
            await connection.OpenAsync();

            var query = $"INSERT INTO `{_api?.DatabaseTableName}_hits` (`SteamID`) VALUES (@SteamId);";

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
            await using var connection = new MySqlConnection(_api?.DatabaseConnectionString);
            await connection.OpenAsync();

            var updateQuery = $@"
                UPDATE `{_api?.DatabaseTableName}_hits`
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

            var hits = Hits[slot];
            await connection.ExecuteAsync(updateQuery, new
            {
                DmgHealth = hits[HitData.HdDmgHealth],
                DmgArmor = hits[HitData.HdDmgArmor],
                Head = hits[HitData.HdHitHead],
                Chest = hits[HitData.HdHitChest],
                Belly = hits[HitData.HdHitBelly],
                LeftArm = hits[HitData.HdHitLeftArm],
                RightArm = hits[HitData.HdHitRightArm],
                LeftLeg = hits[HitData.HdHitLeftLeg],
                RightLeg = hits[HitData.HdHitRightLeg],
                Neak = hits[HitData.HdHitNeak],
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
            await using var connection = new MySqlConnection(_api?.DatabaseConnectionString);
            await connection.OpenAsync();

            var steamId = ReplaceFirstCharacter(id.SteamId2);

            var query = $"SELECT * FROM `{_api?.DatabaseTableName}_hits` WHERE SteamId = @SteamId;";
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
                [HitData.HdHitNeak] = hitsData.Neak,
                [HitData.HdHitAll] = 0
            };

            foreach (var (key, value) in Hits[slot])
            {
                Hits[slot][HitData.HdHitAll] += value;
            }
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