using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using RanksApi;

namespace Ranks;

[MinimumApiVersion(210)]
public class Ranks : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleDescription => "Adds a rating system to the server";
    public override string ModuleName => "[Ranks] Core";
    public override string ModuleVersion => "v2.0.4";

    public string DbConnectionString = string.Empty;

    public Config Config = null!;
    public Database Database = null!;
    public RanksApi RanksApi = null!;

    public readonly ConcurrentDictionary<ulong, User> Users = new();
    private readonly DateTime[] _loginTime = new DateTime[64];

    public override void Load(bool hotReload)
    {
        RanksApi = new RanksApi(this, ModuleDirectory);
        Capabilities.RegisterPluginCapability(IRanksApi.Capability, () => RanksApi);

        Config = LoadConfig();
        DbConnectionString = BuildConnectionString();
        Database = new Database(this, DbConnectionString);
        Task.Run(Database.CreateTable);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);

            if (player is null || player.IsBot) return;
            var playerName = player.PlayerName;

            Task.Run(() => OnClientAuthorizedAsync(playerName, id));
            _loginTime[player.Slot] = DateTime.UtcNow;
        });

        RegisterEventHandler<EventRoundMvp>(EventRoundMvp);
        RegisterEventHandler<EventPlayerDeath>(EventPlayerDeath);
        RegisterEventHandler<EventWeaponFire>((@event, _) =>
        {
            var player = @event.Userid;
            if (player != null)
                UpdateUserStatsLocal(player, exp: -1, hits: 1);

            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerHurt>((@event, _) =>
        {
            var attacker = @event.Attacker;

            if (attacker is { IsValid: true, IsBot: false })
            {
                UpdateUserStatsLocal(attacker, exp: -1, shoots: 1);
            }

            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerDisconnect>((@event, _) =>
        {
            var player = @event.Userid;

            if (player is null || !player.IsValid) return HookResult.Continue;

            if (Users.TryGetValue(player.SteamID, out var user))
            {
                var steamId = new SteamID(player.SteamID);
                var totalTime = GetTotalTime(player.Slot);
                _loginTime[player.Slot] = DateTime.MinValue;

                user.name = player.PlayerName;
                Task.Run(() =>
                    Database.UpdateUserStatsDb(steamId, user, totalTime, DateTimeOffset.Now.ToUnixTimeSeconds()));
                Users.Remove(player.SteamID, out var _);
            }

            return HookResult.Continue;
        });

        AddCommandListener("say", CommandListener_Say);
        AddCommandListener("say_team", CommandListener_Say);

        AddTimer(5.0f, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(u => u.IsValid))
            {
                if (!Users.TryGetValue(player.SteamID, out var user)) continue;

                var steamId = new SteamID(player.SteamID);
                var totalTime = GetTotalTime(player.Slot);

                Task.Run(() =>
                    Database.UpdateUserStatsDb(steamId, user, totalTime, DateTimeOffset.Now.ToUnixTimeSeconds()));
                //Task.Run(OnMapStartAsync);
            }
        }, TimerFlags.REPEAT);


        RoundEvent();
        BombEvents();
        CreateMenu();
    }

    public readonly FakeConVar<bool> RanksEnable = new("css_lr_enable", "", true);

    private void OnMapStart(string mapName)
    {
    }

    private HookResult CommandListener_Say(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return HookResult.Continue;
        var msg = Utils.GetTextInsideQuotes(info.ArgString);

        if (msg.StartsWith('!') || msg.StartsWith('/')) return HookResult.Continue;

        if (Config.UseCommandWithoutPrefix)
        {
            switch (msg)
            {
                case "rank":
                    OnCmdRank(player, info);
                    return HookResult.Continue;
                case "top":
                    OnCmdTop(player, info);
                    return HookResult.Continue;
            }
        }

        return HookResult.Continue;
    }

    private async Task ResetRank(int slot, string name, SteamID steamId)
    {
        var steamId2 = steamId.SteamId2.ReplaceFirstCharacter();

        await Database.ResetPlayerData(steamId2);
        Users[steamId.SteamId64] = new User
        {
            steam = steamId2,
            name = name,
            value = Config.InitialExperiencePoints
        };
        _loginTime[slot] = DateTime.UtcNow;
    }

    private async Task OnClientAuthorizedAsync(string playerName, SteamID steamId)
    {
        var userExists = await Database.UserExists(steamId.SteamId2);

        if (!userExists)
            await Database.AddUserToDb(playerName, steamId.SteamId2);

        var user = await Database.GetUserStatsFromDb(steamId.SteamId2);

        if (user == null) return;

        var initPoints = Config.InitialExperiencePoints;
        if (user.value <= 0 && initPoints > 0)
            user.value = initPoints;

        Users[steamId.SteamId64] = new User
        {
            steam = user.steam,
            name = user.name,
            value = user.value,
            rank = user.rank,
            kills = user.kills,
            deaths = user.deaths,
            shoots = user.shoots,
            hits = user.hits,
            headshots = user.headshots,
            assists = user.assists,
            round_win = user.round_win,
            round_lose = user.round_lose,
            playtime = user.playtime,
            lastconnect = user.lastconnect
        };
    }

    [ConsoleCommand("css_rank")]
    public void OnCmdRank(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null) return;

        var steamId = new SteamID(controller.SteamID);
        Task.Run(() => GetUserStats(controller, steamId));
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(2, "<username or #userid> [exp (def. 0)]")]
    [ConsoleCommand("css_lr_giveexp")]
    public void OnCmdGiveExp(CCSPlayerController? player, CommandInfo info)
    {
        var target = Utils.GetPlayer(info.GetArg(1));

        if (target == null)
        {
            ReplyToCommand(player, "Target is not valid!");
            return;
        }

        var exp = 0;
        if (info.ArgCount >= 3)
            exp = int.TryParse(info.GetArg(2), out var value) ? value : 0;

        RanksApi.GivePlayerExperience(target, exp);
        ReplyToCommand(player, Localizer["command.give_exp", exp, target.PlayerName]);
        ReplyToCommand(target, Localizer["command.target.give_exp", player?.PlayerName ?? "Console", exp]);
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(2, "<username or #userid> [exp (def. 0)]")]
    [ConsoleCommand("css_lr_takeexp")]
    public void OnCmdTakeExp(CCSPlayerController? player, CommandInfo info)
    {
        var target = Utils.GetPlayer(info.GetArg(1));

        if (target == null)
        {
            ReplyToCommand(player, "Target is not valid!");
            return;
        }

        var exp = 0;
        if (info.ArgCount >= 3)
            exp = int.TryParse(info.GetArg(2), out var value) ? value : 0;

        RanksApi.TakePlayerExperience(target, exp);
        ReplyToCommand(player, Localizer["command.take_exp", exp, target.PlayerName]);
        ReplyToCommand(target, Localizer["command.target.take_exp", player?.PlayerName ?? "Console", exp]);
    }

    [ConsoleCommand("css_lr_reload")]
    public void OnCmdReloadCfg(CCSPlayerController? controller, CommandInfo info)
    {
        if (controller != null) return;

        Config = LoadConfig();
        Logger.LogInformation("Configuration successfully rebooted");
    }

    [ConsoleCommand("css_top")]
    public void OnCmdTop(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null) return;

        Task.Run(() => ShowTopPlayers(controller));
    }

    private async Task ShowTopPlayers(CCSPlayerController controller)
    {
        var topPlayersSorted = await Database.GetTopPlayers();

        if (topPlayersSorted == null) return;

        Server.NextFrame(() =>
        {
            controller.PrintToChat(Localizer["top.Title"]);
            var rank = 1;
            foreach (var player in topPlayersSorted)
            {
                if (!controller.IsValid) continue;

                controller.PrintToChat(
                    $"{rank++}. {ChatColors.Blue}{player.name} \x01[{ChatColors.Olive}{ReplaceColorTags(GetLevelFromExperience(player.value).Name)}\x01] -\x06 Experience: {ChatColors.Blue}{player.value}");
            }
        });
    }

    private HookResult EventPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        var assister = @event.Assister;

        if (Config.MinPlayers > PlayersCount())
            return HookResult.Continue;

        var configEvent = Config.Events.EventPlayerDeath;
        var additionally = Config.Events.Additionally;

        if (attacker is not null && attacker.IsValid)
        {
            if (attacker.IsBot || victim is not null && victim.IsBot)
                return HookResult.Continue;

            if (attacker != victim)
            {
                if (victim is not null && attacker.TeamNum == victim.TeamNum && !Config.TeamKillAllowed)
                    UpdateUserStatsLocal(attacker, Localizer["KillingAnAlly"],
                        exp: configEvent.KillingAnAlly, increase: false);
                else
                {
                    var weaponName = @event.Weapon;

                    if (Regex.Match(weaponName, "knife").Success || Regex.Match(weaponName, "bayonet").Success)
                        weaponName = "knife";

                    UpdateUserStatsLocal(attacker, Localizer["PerKill"], exp: configEvent.Kills, kills: 1);

                    if (@event.Penetrated > 0)
                        UpdateUserStatsLocal(attacker, Localizer["KillingThroughWall"], exp: additionally.Penetrated);
                    if (@event.Thrusmoke)
                        UpdateUserStatsLocal(attacker, Localizer["MurderThroughSmoke"], exp: additionally.Thrusmoke);
                    if (@event.Noscope)
                        UpdateUserStatsLocal(attacker, Localizer["MurderWithoutScope"], exp: additionally.Noscope);
                    if (@event.Headshot)
                        UpdateUserStatsLocal(attacker, Localizer["MurderToTheHead"], exp: additionally.Headshot,
                            headshots: 1);
                    if (@event.Attackerblind)
                        UpdateUserStatsLocal(attacker, Localizer["BlindMurder"], exp: additionally.Attackerblind);
                    if (Config.Weapon.TryGetValue(weaponName, out var exp))
                        UpdateUserStatsLocal(attacker, Localizer["MurderWith", weaponName], exp: exp);
                }
            }
        }

        if (victim is not null && victim.IsValid)
        {
            if (victim.IsBot)
                return HookResult.Continue;

            if (attacker != victim)
                UpdateUserStatsLocal(victim, Localizer["PerDeath"], exp: configEvent.Deaths, increase: false, death: 1);
            else
                UpdateUserStatsLocal(victim, Localizer["suicide"], exp: configEvent.Suicide, increase: false);
        }

        if (assister is not null && assister.IsValid)
        {
            if (assister.IsBot) return HookResult.Continue;
            UpdateUserStatsLocal(assister, Localizer["AssistingInAKill"], exp: configEvent.Assists, assist: 1);
        }

        return HookResult.Continue;
    }

    private HookResult EventRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        UpdateUserStatsLocal(@event.Userid, Localizer["Mvp"],
            exp: Config.Events.EventRoundMvp);

        return HookResult.Continue;
    }

    private void RoundEvent()
    {
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            var playerCount = PlayersCount();
            if (Config.MinPlayers > playerCount && RanksEnable.Value)
            {
                PrintToChatAll(Localizer["NotEnoughPlayers", playerCount, Config.MinPlayers]);
            }

            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundEnd>((@event, _) =>
        {
            if (@event.Reason is (int)RoundEndReason.RoundDraw) return HookResult.Continue;
            var winner = @event.Winner;

            var configEvent = Config.Events.EventRoundEnd;

            if (Config.MinPlayers > PlayersCount()) return HookResult.Continue;

            foreach (var player in Utilities.GetPlayers().Where(u => u is { IsValid: true, IsBot: false }))
            {
                if (player.Team is not CsTeam.Spectator)
                {
                    if (player.TeamNum != winner)
                        UpdateUserStatsLocal(player, Localizer["LosingRound"], exp: configEvent.Loser, roundlose: 1, increase: false);
                    else
                        UpdateUserStatsLocal(player, Localizer["WinningRound"], exp: configEvent.Winner, roundwin: 1);
                }
            }

            return HookResult.Continue;
        });
    }

    private void BombEvents()
    {
        RegisterEventHandler<EventBombDropped>((@event, _) =>
        {
            var configEvent = Config.Events.EventPlayerBomb;
            var player = @event.Userid;

            if (player != null && player.IsValid)
                UpdateUserStatsLocal(player, Localizer["dropping_bomb"], exp: configEvent.DroppedBomb, increase: false);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombDefused>((@event, _) =>
        {
            var configEvent = Config.Events.EventPlayerBomb;
            var player = @event.Userid;

            if (player != null && player.IsValid)
                UpdateUserStatsLocal(player, Localizer["defusing_bomb"], exp: configEvent.DefusedBomb);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombPickup>((@event, _) =>
        {
            var configEvent = Config.Events.EventPlayerBomb;
            var player = @event.Userid;

            if (player != null && player.IsValid)
                UpdateUserStatsLocal(player, Localizer["raising_bomb"], exp: configEvent.PickUpBomb);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombPlanted>((@event, _) =>
        {
            var configEvent = Config.Events.EventPlayerBomb;
            var player = @event.Userid;

            if (player != null && player.IsValid)
                UpdateUserStatsLocal(player, Localizer["planting_bomb"], exp: configEvent.PlantedBomb);
            return HookResult.Continue;
        });
    }

    private void UpdateUserStatsLocal(CCSPlayerController? player, string msg = "",
        int exp = 0, bool increase = true, int kills = 0, int death = 0, int assist = 0,
        int shoots = 0, int hits = 0, int headshots = 0, int roundwin = 0, int roundlose = 0)
    {
        if (!RanksEnable.Value) return;

        var isWarmup = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!
            .WarmupPeriod;
        if (player == null || Config.MinPlayers > PlayersCount() || isWarmup) return;

        if (!Users.TryGetValue(player.SteamID, out var user)) return;

        exp = exp == -1 ? 0 : exp;

        if (increase)
        {
            var newExp = RanksApi.OnPlayerGainedExperience(player, exp);
            user.value += newExp ?? exp;
        }
        else
        {
            var newExp = RanksApi.OnPlayerLostExperience(player, exp);
            user.value -= newExp ?? exp;
        }

        user.kills += kills;
        user.deaths += death;
        user.assists += assist;
        user.round_lose += roundlose;
        user.round_win += roundwin;
        user.headshots += headshots;
        user.hits += hits;
        user.shoots += shoots;

        if (user.value <= 0) user.value = Config.InitialExperiencePoints;

        var nextXp = GetExperienceToNextLevel(player);
        if (exp != 0 && Config.ShowExperienceMessages)
        {
            Server.NextFrame(() => PrintToChat(player,
                $"{(increase ? "\x0C+" : "\x02-")}{exp} XP \x08{msg} {(nextXp == 0 ? string.Empty : $"{Localizer["next_level", nextXp]}")}"));
        }
    }

    public (string Name, int Level) GetLevelFromExperience(long experience)
    {
        foreach (var rank in Config.Ranks.OrderByDescending(pair => pair.Value))
        {
            if (experience >= rank.Value)
            {
                return (rank.Key, Config.Ranks.Count(pair => pair.Value <= rank.Value));
            }
        }

        return (string.Empty, 0);
    }

    private long GetExperienceToNextLevel(CCSPlayerController player)
    {
        if (!Users.TryGetValue(player.SteamID, out var user)) return 0;

        var currentExperience = user.value;
        foreach (var rank in Config.Ranks.OrderBy(pair => pair.Value))
        {
            if (currentExperience < rank.Value)
            {
                var requiredExperience = rank.Value;
                var experienceToNextLevel = requiredExperience - currentExperience;

                var newLevel = GetLevelFromExperience(currentExperience);

                if (newLevel.Level != user.rank)
                {
                    var isUpRank = newLevel.Level > user.rank;

                    var newLevelName = ReplaceColorTags(newLevel.Name);
                    PrintToChat(player, isUpRank
                        ? Localizer["Up", newLevelName]
                        : Localizer["Down", newLevelName]);

                    user.rank = newLevel.Level;
                    RanksApi.OnRankChanged(player, newLevel.Level, isUpRank);
                }

                return experienceToNextLevel;
            }
            else
            {
                var newLevel = GetLevelFromExperience(currentExperience);
                user.rank = newLevel.Level;
            }
        }

        return 0;
    }

    private void CreateMenu()
    {
        AddCommand("css_lvl", "", (player, _) =>
        {
            if (player == null) return;

            var title = Localizer["menu.title"];
            var menu = new CenterHtmlMenu(title, this);
            var ranksMenu = new CenterHtmlMenu(title, this);

            menu.AddMenuOption(Localizer["menu.allranks"], (_, _) => ranksMenu.Open(player));
            menu.AddMenuOption(Localizer["menu.reset"], (_, _) =>
            {
                var subMenu = new CenterHtmlMenu(Localizer["menu.reset.title"], this)
                {
                    PostSelectAction = PostSelectAction.Close
                };

                subMenu.AddMenuOption(Localizer["menu.reset.yes"], (_, _) =>
                {
                    var playerName = player.PlayerName;
                    var steamId = new SteamID(player.SteamID);
                    Task.Run(() => ResetRank(player.Slot, playerName, steamId));
                    PrintToChat(player, Localizer["menu.reset.complete"]);
                });

                subMenu.AddMenuOption(Localizer["menu.reset.no"],
                    (controller, option) => PrintToChat(controller, Localizer["reset.reset.canceled"]));

                subMenu.Open(player);
            }, Config.StatisticsResetEnabled);

            foreach (var (rankName, rankValue) in Config.Ranks)
            {
                ranksMenu.AddMenuOption(Localizer["menu.allranks.show", rankName, rankValue], null!, true);
            }

            menu.Open(player);
        });
    }

    private async Task GetUserStats(CCSPlayerController controller, SteamID steamId)
    {
        if (!Users.TryGetValue(steamId.SteamId64, out var user)) return;

        var totalTime = TimeSpan.FromSeconds(user.playtime);
        //var formattedTime = totalPlayTime.ToString(@"hh\:mm\:ss");
        var formattedTime =
            $"{(totalTime.Days > 0 ? $"{Localizer["days", totalTime.Days]}, " : "")}" +
            $"{(totalTime.Hours > 0 ? $"{Localizer["hours", totalTime.Hours]}, " : "")}" +
            $"{(totalTime.Minutes > 0 ? $"{Localizer["minutes", totalTime.Minutes]}, " : "")}" +
            $"{(totalTime.Seconds > 0 ? $"{Localizer["seconds", totalTime.Seconds]}" : "")}";
        //var currentPlayTime = (DateTime.Now - _loginTime[index]).ToString(@"hh\:mm\:ss");
        var getPlayerTop = await Database.GetPlayerRankAndTotal(steamId.SteamId2);

        Server.NextFrame(() =>
        {
            if (!controller.IsValid) return;

            var headshotPercentage =
                (double)user.headshots / (user.kills + 1) * 100;
            double kdr = 0;
            if (user is { kills: > 0, deaths: > 0 })
                kdr = (double)user.kills / (user.deaths + 1);

            PrintToChat(controller,
                "-------------------------------------------------------------------");
            if (getPlayerTop != null)
                PrintToChat(controller,
                    Localizer["rank.YourPosition", getPlayerTop.PlayerRank, getPlayerTop.TotalPlayers]);

            PrintToChat(controller,
                Localizer["rank.Experience", user.value, ReplaceColorTags(GetLevelFromExperience(user.value).Name)]);
            PrintToChat(controller,
                Localizer["rank.KDA", user.kills, user.headshots, user.deaths, user.assists]);
            PrintToChat(controller, Localizer["rank.Rounds", user.round_win, user.round_lose]);
            PrintToChat(controller,
                Localizer["rank.KDR", headshotPercentage.ToString("0.00"), kdr.ToString("0.00")]);
            PrintToChat(controller, Localizer["rank.PlayTime", formattedTime]);
            PrintToChat(controller,
                "-------------------------------------------------------------------");
        });
    }

    private string BuildConnectionString()
    {
        var dbConfig = Config.Connection;

        Console.WriteLine("Building connection string");
        var builder = new MySqlConnectionStringBuilder
        {
            Database = dbConfig.Database,
            UserID = dbConfig.User,
            Password = dbConfig.Password,
            Server = dbConfig.Host,
            Port = (uint)dbConfig.Port
        };

        Console.WriteLine("OK!");
        return builder.ConnectionString;
    }

    private Config LoadConfig()
    {
        var configPath = Path.Combine(RanksApi.CoreConfigDirectory, "ranks.json");
        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

        return config;
    }

    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            TableName = "lvl_base",
            Prefix = "[ {BLUE}Ranks {DEFAULT}]",
            TeamKillAllowed = true,
            UseCommandWithoutPrefix = true,
            ShowExperienceMessages = true,
            MinPlayers = 4,
            InitialExperiencePoints = 500,
            StatisticsResetEnabled = true,
            Events = new EventsExpSettings
            {
                EventRoundMvp = 12,
                EventPlayerDeath = new PlayerDeath
                    { Kills = 13, Deaths = 20, Assists = 5, KillingAnAlly = 10, Suicide = 15 },
                EventPlayerBomb = new Bomb { DroppedBomb = 5, DefusedBomb = 3, PickUpBomb = 3, PlantedBomb = 4 },
                EventRoundEnd = new RoundEnd { Winner = 5, Loser = 8 },
                Additionally = new Additionally
                    { Headshot = 1, Noscope = 2, Attackerblind = 1, Thrusmoke = 1, Penetrated = 2 }
            },
            Weapon = new Dictionary<string, int>
            {
                ["knife"] = 5,
                ["awp"] = 2
            },
            Ranks = new Dictionary<string, int>
            {
                { "None", 0 },
                { "Silver I", 50 },
                { "Silver II", 100 },
                { "Silver III", 150 },
                { "Silver IV", 300 },
                { "Silver Elite", 400 },
                { "Silver Elite Master", 500 },
                { "Gold Nova I", 600 },
                { "Gold Nova II", 700 },
                { "Gold Nova III", 800 },
                { "Gold Nova Master", 900 },
                { "Master Guardian I", 1000 },
                { "Master Guardian II", 1400 },
                { "Master Guardian Elite", 1600 },
                { "BigStar", 2100 },
                { "Legendary Eagle", 2600 },
                { "Legendary Eagle Master", 2900 },
                { "Supreme", 3400 },
                { "The Global Elite", 4500 }
            },
            Connection = new RankDb
            {
                Host = "HOST",
                Database = "DATABASE_NAME",
                User = "USER_NAME",
                Password = "PASSWORD",
                Port = 3306
            }
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return config;
    }

    public void PrintToChat(CCSPlayerController player, string message)
    {
        if (!player.IsValid) return;

        player.PrintToChat($"{ReplaceColorTags(Config.Prefix)} {message}");
    }

    private void ReplyToCommand(CCSPlayerController? player, string message)
    {
        if (player == null)
            Logger.LogInformation(message);
        else
        {
            if (!player.IsValid) return;
            PrintToChat(player, message);
        }
    }

    public void PrintToChatAll(string message)
    {
        Server.PrintToChatAll($"{ReplaceColorTags(Config.Prefix)} {message}");
    }

    private string ReplaceColorTags(string input)
    {
        return input.ReplaceColorTags();
    }

    private TimeSpan GetTotalTime(int slot)
    {
        var totalTime = DateTime.UtcNow - _loginTime[slot];

        _loginTime[slot] = DateTime.UtcNow;

        return totalTime;
    }

    private static int PlayersCount()
    {
        return Utilities.GetPlayers().Count(u => u is
        {
            IsBot: false, IsValid: true, TeamNum: not (0 or 1), PlayerPawn.Value: not null,
            PlayerPawn.Value.IsValid: true
        });
    }
}

public class PlayerStats
{
    public int PlayerRank { get; init; }
    public int TotalPlayers { get; init; }
}

public class Config
{
    public required string TableName { get; init; }
    public required string Prefix { get; init; }
    public bool TeamKillAllowed { get; init; }
    public bool UseCommandWithoutPrefix { get; init; }
    public bool ShowExperienceMessages { get; init; }
    public int MinPlayers { get; init; }
    public int InitialExperiencePoints { get; init; }
    public bool StatisticsResetEnabled { get; init; }
    public EventsExpSettings Events { get; init; } = null!;
    public Dictionary<string, int> Weapon { get; init; } = null!;
    public Dictionary<string, int> Ranks { get; init; } = null!;
    public RankDb Connection { get; init; } = null!;
}