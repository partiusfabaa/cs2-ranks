using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using MySqlConnector;

namespace Ranks;

[MinimumApiVersion(84)]
public class Ranks : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleDescription => "Adds a rating system to the server";
    public override string ModuleName => "Ranks";
    public override string ModuleVersion => "v1.0.4";

    private static string _dbConnectionString = string.Empty;

    private Config _config = null!;

    private readonly bool?[] _userRankReset = new bool?[Server.MaxPlayers];

    private readonly Dictionary<ulong, User> _users = new();
    private readonly DateTime[] _loginTime = new DateTime[Server.MaxPlayers];

    private enum PrintTo
    {
        Chat = 1,
        ChatAll,
        Console
    }

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();
        _dbConnectionString = BuildConnectionString();
        Task.Run(CreateTable);

        RegisterListener<Listeners.OnClientConnected>(slot =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (!player.IsBot)
            {
                Task.Run(() => OnClientConnectedAsync(slot, new SteamID(player.SteamID)));
                _loginTime[player.Index] = DateTime.Now;
                _userRankReset[player.Index] = false;
            }
        });

        RegisterEventHandler<EventRoundMvp>(EventRoundMvp);
        RegisterEventHandler<EventPlayerDeath>(EventPlayerDeath);
        RegisterEventHandler<EventPlayerDisconnect>((@event, _) =>
        {
            var player = @event.Userid;
            var entityIndex = player.Index;

            if (_userRankReset[entityIndex] != null) _userRankReset[entityIndex] = null;

            if (_users.TryGetValue(player.SteamID, out var user))
            {
                Task.Run(() => UpdateUserStatsDb(new SteamID(player.SteamID), user, GetTotalTime(entityIndex)));
                _users.Remove(player.SteamID);
            }

            return HookResult.Continue;
        });

        AddCommandListener("say", CommandListener_Say);
        AddCommandListener("say_team", CommandListener_Say);

        AddTimer(1.0f, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(player => player.IsValid))
            {
                if (!_users.TryGetValue(player.SteamID, out var user)) continue;
                if (!user.clan_tag_enabled || !_config.EnableScoreBoardRanks) continue;

                player.Clan = $"[{GetLevelFromExperience(user.experience).Name}]";
            }
        }, TimerFlags.REPEAT);
        AddTimer(300.0f, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(u => u.IsValid))
            {
                if (!_users.TryGetValue(player.SteamID, out var user)) continue;

                var entityIndex = player.Index;
                Task.Run(() => UpdateUserStatsDb(new SteamID(player.SteamID), user, GetTotalTime(entityIndex)));
            }
        }, TimerFlags.REPEAT);

        RoundEvent();
        BombEvents();
        CreateMenu();
    }

    private HookResult CommandListener_Say(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return HookResult.Continue;

        var msg = GetTextInsideQuotes(info.ArgString);
        if (_config.UseCommandWithoutPrefix)
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

        if (_userRankReset[player.Index] != null)
        {
            if (_userRankReset[player.Index]!.Value)
            {
                _userRankReset[player.Index] = false;
                switch (msg)
                {
                    case "confirm":
                        SendMessageToSpecificChat(player, "The rank has been successfully reset!");
                        Task.Run(() => ResetRank(player));
                        return HookResult.Handled;
                    case "cancel":
                        SendMessageToSpecificChat(player, "Rank reset canceled. Operation aborted.");
                        return HookResult.Handled;
                }
            }
        }

        return HookResult.Continue;
    }

    private async Task ResetRank(CCSPlayerController player)
    {
        var steamId = new SteamID(player.SteamID);
        await ResetPlayerData(steamId.SteamId2);
        _users[steamId.SteamId64] = new User
        {
            username = player.PlayerName,
            steamid = steamId.SteamId2
        };

        _loginTime[player.Index] = DateTime.Now;
    }

    private string GetTextInsideQuotes(string input)
    {
        var startIndex = input.IndexOf('"');
        var endIndex = input.LastIndexOf('"');

        if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
        {
            return input.Substring(startIndex + 1, endIndex - startIndex - 1);
        }

        return string.Empty;
    }

    private async Task OnClientConnectedAsync(int slot, SteamID steamId)
    {
        var player = Utilities.GetPlayerFromSlot(slot);

        var userExists = await UserExists(steamId);

        if (!userExists)
            if (!player.IsBot)
                await AddUserToDb(player);

        var user = await GetUserStatsFromDb(steamId);

        if (user == null) return;

        _users[steamId.SteamId64] = new User
        {
            username = player.PlayerName,
            steamid = user.steamid,
            experience = user.experience,
            score = user.score,
            kills = user.kills,
            deaths = user.deaths,
            assists = user.assists,
            noscope_kills = user.noscope_kills,
            damage = user.damage,
            mvp = user.mvp,
            headshot_kills = user.headshot_kills,
            percentage_headshot = user.percentage_headshot,
            kdr = user.kdr,
            play_time = user.play_time,
            last_level = user.last_level,
            clan_tag_enabled = user.clan_tag_enabled
        };
    }

    [ConsoleCommand("css_lr_reload")]
    public void OnCmdReloadCfg(CCSPlayerController? controller, CommandInfo info)
    {
        if (controller != null) return;

        _config = LoadConfig();

        SendMessageToSpecificChat(msg: "Configuration successfully rebooted", print: PrintTo.Console);
    }

    private HookResult EventPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        var assister = @event.Assister;

        if (_config.MinPlayers > PlayersCount())
            return HookResult.Continue;

        var configEvent = _config.Events.EventPlayerDeath;
        var additionally = _config.Events.Additionally;

        if (attacker.IsValid)
        {
            if (attacker.IsBot || victim is { PlayerName: null } || attacker is { PlayerName: null })
                return HookResult.Continue;

            if (attacker.PlayerName != victim.PlayerName)
            {
                if (attacker.TeamNum == victim.TeamNum)
                    UpdateUserStatsLocal(attacker, $"XP for killing an ally",
                        exp: configEvent.KillingAnAlly, increase: false);
                else
                {
                    var weaponName = @event.Weapon;

                    if (Regex.Match(weaponName, "knife").Success)
                        weaponName = "knife";

                    UpdateUserStatsLocal(attacker, "XP per kill", exp: configEvent.Kills, kills: 1,
                        dmg: @event.DmgHealth);

                    if (@event.Penetrated > 0)
                        UpdateUserStatsLocal(attacker, "XP for killing through the wall",
                            exp: additionally.Penetrated);
                    if (@event.Thrusmoke)
                        UpdateUserStatsLocal(attacker, "XP for murder through smoke", exp: additionally.Thrusmoke);
                    if (@event.Noscope)
                        UpdateUserStatsLocal(attacker, "XP for murder without a scope", exp: additionally.Noscope,
                            nzKills: @event.Noscope ? 1 : 0);
                    if (@event.Headshot)
                        UpdateUserStatsLocal(attacker, "XP for a murder to the head", exp: additionally.Headshot,
                            headKills: @event.Headshot ? 1 : 0, headshot: @event.Headshot);
                    if (@event.Attackerblind)
                        UpdateUserStatsLocal(attacker, "XP for the blind murder", exp: additionally.Attackerblind);
                    if (_config.Weapon.TryGetValue(weaponName, out var exp))
                        UpdateUserStatsLocal(attacker, $"XP for murder with a {weaponName}", exp: exp);
                }
            }
        }

        if (victim.IsValid)
        {
            if (victim.IsBot || victim is { PlayerName: null })
                return HookResult.Continue;

            UpdateUserStatsLocal(victim, "XP per death \x02:(\x08", exp: configEvent.Deaths, increase: false,
                death: 1);
        }

        if (assister.IsValid)
        {
            if (assister.IsBot || assister is { PlayerPawn: null }) return HookResult.Continue;

            UpdateUserStatsLocal(assister, "XP for assisting in a kill", exp: configEvent.Assists, assist: 1);
        }

        return HookResult.Continue;
    }

    private void UpdateUserStatsLocal(CCSPlayerController player, string msg = "",
        int exp = 0, bool increase = true, int kills = 0, int death = 0, int assist = 0,
        int nzKills = 0, int dmg = 0, int mvp = 0, int headKills = 0, bool headshot = false)
    {
        if (_config.MinPlayers > PlayersCount() ||
            Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!
                .WarmupPeriod) return;

        if (!_users.TryGetValue(player.SteamID, out var user)) return;

        user.username = player.PlayerName;

        exp = exp == -1 ? 0 : exp;

        if (increase)
            user.experience += exp;
        else
            user.experience -= exp;

        if (increase)
            user.score ++;
        else
            user.score --;

        user.kills += kills;
        user.deaths += death;
        user.assists += assist;
        user.noscope_kills += nzKills;
        user.damage += dmg;
        user.mvp += mvp;
        user.headshot_kills += headKills;

        var headshotPercentage =
            (double)(user.headshot_kills + (headshot ? 1 : 0)) / (user.kills + 1) * 100;

        user.percentage_headshot = headshotPercentage;

        double kdr = 0;
        if (user is { kills: > 0, deaths: > 0 })
            kdr = (double)user.kills / (user.deaths + 1);
        user.kdr = kdr;

        if (user.experience <= 0) user.experience = 0;

        var nextXp = GetExperienceToNextLevel(player);
        if (exp != 0 && _config.ShowExperienceMessages)
            Server.NextFrame(() => SendMessageToSpecificChat(player,
                $"{(increase ? "\x0C+" : "\x02-")}{exp} \x08{msg} {(nextXp == 0 ? "" : $"(Next level: \x06{nextXp}\x08)")}"));
    }

    private (string Name, int Level) GetLevelFromExperience(long experience)
    {
        foreach (var rank in _config.Ranks.OrderByDescending(pair => pair.Value))
        {
            if (experience >= rank.Value)
                return (rank.Key, _config.Ranks.Count(pair => pair.Value <= rank.Value));
        }

        return (string.Empty, 0);
    }

    private long GetExperienceToNextLevel(CCSPlayerController player)
    {
        if (!_users.TryGetValue(player.SteamID, out var user)) return 0;

        var currentExperience = user.experience;

        foreach (var rank in _config.Ranks.OrderBy(pair => pair.Value))
        {
            if (currentExperience < rank.Value)
            {
                var requiredExperience = rank.Value;
                var experienceToNextLevel = requiredExperience - currentExperience;

                var newLevel = GetLevelFromExperience(currentExperience);

                if (newLevel.Level != user.last_level)
                {
                    var isUpRank = newLevel.Level > user.last_level;
                    SendMessageToSpecificChat(player, isUpRank
                        ? $"Congratulations! You've reached level \x06[ {newLevel.Name} ]"
                        : $"Oh no! Your level has decreased to \x02[ {newLevel.Name} ]");

                    if (_config.EnableScoreBoardRanks && user.clan_tag_enabled)
                        player.Clan = $"[{newLevel.Name}]";

                    user.last_level = newLevel.Level;
                }

                return experienceToNextLevel;
            }
        }

        return 0;
    }

    private void CreateMenu()
    {
        var title = "\x08--[ \x0CRanks \x08]--";
        var menu = new ChatMenu(title);

        var ranksMenu = new ChatMenu(title);
        menu.AddMenuOption("Ranks", (player, _) => ChatMenus.OpenMenu(player, ranksMenu));
        menu.AddMenuOption("Reset Rank", (player, _) =>
        {
            _userRankReset[player.Index] = true;
            SendMessageToSpecificChat(player,
                "If you do agree to reset the rank to zero, write\x06 confirm\x01. If you want to cancel, write\x02 cancel");
        });

        foreach (var rank in _config.Ranks)
        {
            var rankName = rank.Key;
            var rankValue = rank.Value;
            ranksMenu.AddMenuOption($" \x0C{rankName} \x08- \x06{rankValue}\x08 experience",
                (_, _) => Server.PrintToChatAll(""), true);
        }

        AddCommand("css_lvl", "", (player, _) =>
        {
            if (player == null) return;
            ChatMenus.OpenMenu(player, menu);
        });
    }

    [ConsoleCommand("css_top")]
    public void OnCmdTop(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null) return;

        foreach (var players in Utilities.GetPlayers().Where(u => u is { IsBot: false, IsValid: true }))
        {
            var entityIndex = players.Index;

            if (!_users.TryGetValue(players.SteamID, out var user)) return;

            Task.Run(() => UpdateUserStatsDb(new SteamID(players.SteamID), user, GetTotalTime(entityIndex)));
        }

        AddTimer(.5f, () => Task.Run(() => ShowTopPlayers(controller)));
    }

    private async Task ShowTopPlayers(CCSPlayerController controller)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT DISTINCT * FROM `ranks_users` ORDER BY `experience` DESC LIMIT 10;";

            var topPlayers = await connection.QueryAsync<User>(query);

            var rank = 1;
            foreach (var player in topPlayers)
            {
                var level = GetLevelFromExperience(player.experience);

                controller.PrintToChat(
                    $"{rank}. {ChatColors.Blue}{player.username} \x01[{ChatColors.Olive}{level.Name}\x01] -\x06 Experience: {ChatColors.Blue}{player.experience}\x01,\x06 K/D:{ChatColors.Blue} {player.kdr:F1}");
                rank ++;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    [ConsoleCommand("css_rank")]
    public void OnCmdRank(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null) return;

        Task.Run(() => GetUserStats(controller));
    }

    [ConsoleCommand("css_rank_tag")]
    public void ToggleRank(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !_config.EnableScoreBoardRanks) return;

        if (!_users.TryGetValue(player.SteamID, out var user)) return;

        user.clan_tag_enabled ^= true;

        if (!user.clan_tag_enabled)
        {
            SendMessageToSpecificChat(player, "Tag\x02 disabled");
            player.Clan = string.Empty;
            return;
        }

        SendMessageToSpecificChat(player, "Tag\x06 enabled");
        player.Clan = GetLevelFromExperience(user.experience).Name;
    }

    private async void GetUserStats(CCSPlayerController controller)
    {
        if (!_users.TryGetValue(controller.SteamID, out var user)) return;

        var index = controller.Index;

        var totalPlayTime = TimeSpan.FromSeconds(user.play_time);
        var formattedTime = totalPlayTime.ToString(@"hh\:mm\:ss");
        var currentPlayTime = (DateTime.Now - _loginTime[index]).ToString(@"hh\:mm\:ss");
        var getPlayerTop = await GetPlayerRankAndTotal(new SteamID(controller.SteamID));

        SendMessageToSpecificChat(controller, "-------------------------------------------------------------------");
        if (getPlayerTop != null)
        {
            SendMessageToSpecificChat(controller,
                $" \x08 Your position in the top: \x0C#{getPlayerTop.PlayerRank}/{getPlayerTop.TotalPlayers}");
        }

        SendMessageToSpecificChat(controller,
            $" \x08 Experience: \x04{user.experience} \x08| Score: \x04{user.score}");
        SendMessageToSpecificChat(controller,
            $" \x08 Kills: \x04{user.kills} \x08| Deaths: \x04{user.deaths} \x08| Assists: \x04{user.assists}");
        SendMessageToSpecificChat(controller,
            $" \x08 Headshot Kills: \x04{user.headshot_kills} \x08| Noscope Kills: \x04{user.noscope_kills}");
        SendMessageToSpecificChat(controller,
            $" \x08 Damage: \x04{user.damage} \x08| Mvp: \x04{user.mvp}");
        SendMessageToSpecificChat(controller,
            $" \x08 Percentage Headshot: \x04{user.percentage_headshot:0.00} \x08| KD: \x04{user.kdr:0.00}");
        SendMessageToSpecificChat(controller,
            $" \x08 Current Play Time: \x04{currentPlayTime} \x08| Total Play Time: \x04{formattedTime}");
        SendMessageToSpecificChat(controller, "-------------------------------------------------------------------");
    }

    private HookResult EventRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        UpdateUserStatsLocal(@event.Userid, $"XP for the MVP of this round",
            exp: _config.Events.EventRoundMvp, mvp: 1);

        return HookResult.Continue;
    }

    private void RoundEvent()
    {
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            var playerCount = PlayersCount();
            if (_config.MinPlayers > playerCount)
            {
                SendMessageToSpecificChat(msg:
                    $" \x08There are not enough players on the server:\x0C {playerCount}\x08 out of\x0C {_config.MinPlayers}\x08. Gaining experience is temporarily unavailable",
                    print: PrintTo.ChatAll);
            }

            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundEnd>((@event, _) =>
        {
            var winner = @event.Winner;

            var configEvent = _config.Events.EventRoundEnd;

            if (_config.MinPlayers > PlayersCount()) return HookResult.Continue;

            for (var i = 1; i < Server.MaxPlayers; i ++)
            {
                var player = Utilities.GetPlayerFromIndex(i);

                if (player is { IsValid: true, IsBot: false })
                {
                    if (player.TeamNum != (int)CsTeam.Spectator)
                    {
                        if (player.TeamNum != winner)
                            UpdateUserStatsLocal(player, "XP for losing a round", exp: configEvent.Loser,
                                increase: false);
                        else
                            UpdateUserStatsLocal(player, "XP for winning the round", exp: configEvent.Winner);
                    }
                }
            }

            return HookResult.Continue;
        });
    }

    private void BombEvents()
    {
        var configEvent = _config.Events.EventPlayerBomb;
        RegisterEventHandler<EventBombDropped>((@event, _) =>
        {
            UpdateUserStatsLocal(@event.Userid, "XP for dropping the bomb", exp: configEvent.DroppedBomb,
                increase: false);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombDefused>((@event, _) =>
        {
            UpdateUserStatsLocal(@event.Userid, "XP for defusing the bomb", exp: configEvent.DefusedBomb);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombPickup>((@event, _) =>
        {
            UpdateUserStatsLocal(@event.Userid, "XP for raising the bomb", exp: configEvent.PickUpBomb);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventBombPlanted>((@event, _) =>
        {
            UpdateUserStatsLocal(@event.Userid, "XP for planting the bomb", exp: configEvent.PlantedBomb);
            return HookResult.Continue;
        });
    }

    private async Task UpdateUserStatsDb(SteamID steamId, User user, TimeSpan playtime)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var updateQuery = @"
        UPDATE 
            `ranks_users` 
        SET 
            `username` = @Username,
            `experience` = @Experience,
            `score` = @Score,
            `kills` = @Kills,
            `deaths` = @Deaths,
            `assists` = @Assists,
            `noscope_kills` = @NoscopeKills,
            `damage` = @Damage,
            `mvp` = @Mvp,
            `headshot_kills` = @HeadshotKills,
            `percentage_headshot` = @PercentageHeadshot,
            `kdr` = @Kdr,
            `last_active` = NOW(),
            `play_time` = `play_time` + @PlayTime,
            `last_level` = @LastLevel,
            `clan_tag_enabled` = @ClanTagEnabled
        WHERE `steamid` = @SteamId";

            await connection.ExecuteAsync(updateQuery, new
            {
                Username = user.username,
                SteamId = steamId.SteamId2,
                Experience = user.experience,
                Score = user.score,
                Kills = user.kills,
                Deaths = user.deaths,
                Assists = user.assists,
                NoscopeKills = user.noscope_kills,
                Damage = user.damage,
                Mvp = user.mvp,
                HeadshotKills = user.headshot_kills,
                PercentageHeadshot = user.percentage_headshot,
                Kdr = user.kdr,
                LastLevel = user.last_level,
                PlayTime = (int)playtime.TotalSeconds,
                ClanTagEnabled = user.clan_tag_enabled
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task<PlayerStats?> GetPlayerRankAndTotal(SteamID steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var rankQuery =
                "SELECT COUNT(*) + 1 AS PlayerRank FROM ranks_users WHERE experience > (SELECT experience FROM ranks_users WHERE steamid = @SteamId);";

            var totalPlayersQuery = "SELECT COUNT(*) AS TotalPlayers FROM ranks_users;";

            var playerRank =
                await connection.QueryFirstOrDefaultAsync<int>(rankQuery, new { SteamId = steamId.SteamId2 });
            var totalPlayers = await connection.QueryFirstOrDefaultAsync<int>(totalPlayersQuery);

            return new PlayerStats { PlayerRank = playerRank, TotalPlayers = totalPlayers };
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    private async Task AddUserToDb(CCSPlayerController player)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var parameters = new User
            {
                username = player.PlayerName,
                steamid = new SteamID(player.SteamID).SteamId2,
                experience = 0,
                score = 0,
                kills = 0,
                deaths = 0,
                assists = 0,
                noscope_kills = 0,
                damage = 0,
                mvp = 0,
                headshot_kills = 0,
                percentage_headshot = 0,
                kdr = 0.0,
                last_active = DateTime.Now,
                play_time = 0,
                last_level = 0,
                clan_tag_enabled = true
            };

            var query = @"
                INSERT INTO `ranks_users` 
                (`username`, `steamid`, `experience`, `score`, `kills`, `deaths`, `assists`, `noscope_kills`, `damage`, `mvp`, `headshot_kills`, `percentage_headshot`, `kdr`, `last_active`, `play_time`, `last_level`, `clan_tag_enabled`) 
                VALUES 
                (@username, @steamid, @Experience, @score, @kills, @deaths, @assists, @noscope_kills, @damage, @mvp, @headshot_kills, @percentage_headshot, @kdr, @last_active, @play_time, @last_level, @clan_tag_enabled);";

            await connection.ExecuteAsync(query, parameters);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    static async Task ResetPlayerData(string steamId)
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            dbConnection.Open();

            var resetPlayerQuery = @"
                UPDATE ranks_users
                SET
                    experience = 0,
                    score = 0,
                    kills = 0,
                    deaths = 0,
                    assists = 0,
                    noscope_kills = 0,
                    damage = 0,
                    mvp = 0,
                    headshot_kills = 0,
                    percentage_headshot = 0,
                    kdr = 0,
                    last_active = NOW(),
                    play_time = 0,
                    last_level = 0
                WHERE steamId = @SteamId;";

            await dbConnection.ExecuteAsync(resetPlayerQuery, new { SteamId = steamId });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    static async Task CreateTable()
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            dbConnection.Open();

            var createLrTable = @"
            CREATE TABLE IF NOT EXISTS `ranks_users` (
                `username` VARCHAR(255) NOT NULL,
                `steamid` VARCHAR(255) NOT NULL,
                `experience` BIGINT NOT NULL,
                `score` BIGINT NOT NULL,
                `kills` BIGINT NOT NULL,
                `deaths` BIGINT NOT NULL,
                `assists` BIGINT NOT NULL,
                `noscope_kills` BIGINT NOT NULL,
                `damage` BIGINT NOT NULL,
                `mvp` BIGINT NOT NULL,
                `headshot_kills` BIGINT NOT NULL,
                `percentage_headshot` DOUBLE NOT NULL,
                `kdr` DOUBLE NOT NULL,
                `last_active` DATETIME NOT NULL,
                `play_time` BIGINT NOT NULL,
                `last_level` INT NOT NULL DEFAULT 0,
                `clan_tag_enabled` BOOL NOT NULL
            );";

            await dbConnection.ExecuteAsync(createLrTable);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private TimeSpan GetTotalTime(uint entityIndex)
    {
        var currentTime = DateTime.Now;
        var totalTime = currentTime - _loginTime[entityIndex];

        _loginTime[entityIndex] = currentTime;

        return totalTime;
    }

    private async Task<User?> GetUserStatsFromDb(SteamID steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();
            var user = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM `ranks_users` WHERE `steamid` = @SteamId", new { SteamId = steamId.SteamId2 });

            return user;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null;
    }

    private string BuildConnectionString()
    {
        var dbConfig = LoadConfig();

        Console.WriteLine("Building connection string");
        var builder = new MySqlConnectionStringBuilder
        {
            Database = dbConfig.Connection.Database,
            UserID = dbConfig.Connection.User,
            Password = dbConfig.Connection.Password,
            Server = dbConfig.Connection.Host,
            Port = 3306
        };

        Console.WriteLine("OK!");
        return builder.ConnectionString;
    }

    private Config LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "settings_ranks.json");
        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

        return config;
    }

    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            Prefix = "[ {BLUE}Ranks {DEFAULT}]",
            EnableScoreBoardRanks = true,
            UseCommandWithoutPrefix = true,
            ShowExperienceMessages = true,
            MinPlayers = 4,
            Events = new EventsExpSettings
            {
                EventRoundMvp = 12,
                EventPlayerDeath = new PlayerDeath { Kills = 15, Deaths = 20, Assists = 5, KillingAnAlly = 10 },
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
                Password = "PASSWORD"
            }
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return config;
    }

    private async Task<bool> UserExists(SteamID steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var exists = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM `ranks_users` WHERE `steamid` = @SteamId)",
                new { SteamId = steamId.SteamId2 });

            return exists;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return false;
    }

    private void SendMessageToSpecificChat(CCSPlayerController handle = null!, string msg = "",
        PrintTo print = PrintTo.Chat)
    {
        var colorText = ReplaceColorTags(_config.Prefix);

        switch (print)
        {
            case PrintTo.Chat:
                handle.PrintToChat($"{colorText} {msg}");
                return;
            case PrintTo.ChatAll:
                Server.PrintToChatAll($"{colorText} {msg}");
                return;
            case PrintTo.Console:
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"{colorText} {msg}");
                Console.ResetColor();
                return;
        }
    }

    private string ReplaceColorTags(string input)
    {
        string[] colorPatterns =
        {
            "{DEFAULT}", "{RED}", "{LIGHTPURPLE}", "{GREEN}", "{LIME}", "{LIGHTGREEN}", "{LIGHTRED}", "{GRAY}",
            "{LIGHTOLIVE}", "{OLIVE}", "{LIGHTBLUE}", "{BLUE}", "{PURPLE}", "{GRAYBLUE}"
        };
        string[] colorReplacements =
        {
            "\x01", "\x02", "\x03", "\x04", "\x05", "\x06", "\x07", "\x08", "\x09", "\x10", "\x0B", "\x0C", "\x0E",
            "\x0A"
        };

        for (var i = 0; i < colorPatterns.Length; i ++)
            input = input.Replace(colorPatterns[i], colorReplacements[i]);

        return input;
    }

    private int PlayersCount()
    {
        return Utilities.GetPlayers().Count(u => u is { IsBot: false, IsValid: true });
    }
}

public class PlayerStats
{
    public int PlayerRank { get; init; }
    public int TotalPlayers { get; init; }
}

public class Config
{
    public required string Prefix { get; init; }
    public bool EnableScoreBoardRanks { get; init; }
    public bool UseCommandWithoutPrefix { get; init; }
    public bool ShowExperienceMessages { get; init; }
    public int MinPlayers { get; init; }
    public EventsExpSettings Events { get; set; } = null!;
    public Dictionary<string, int> Weapon { get; set; } = null!;
    public Dictionary<string, int> Ranks { get; set; } = null!;
    public RankDb Connection { get; set; } = null!;
}