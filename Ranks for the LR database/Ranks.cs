using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using MySqlConnector;

namespace Ranks;

[MinimumApiVersion(107)]
public class Ranks : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleDescription => "Adds a rating system to the server";
    public override string ModuleName => "Ranks";
    public override string ModuleVersion => "v1.0.5";

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

        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);

            if (player.IsBot) return;
            var playerName = player.PlayerName;
            var steamId = id;

            Task.Run(() => OnClientAuthorizedAsync(playerName, steamId));
            _loginTime[player.Index] = DateTime.Now;
            _userRankReset[player.Index] = false;
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
            var player = @event.Userid;

            if (attacker != null && player != null && attacker != player)
                UpdateUserStatsLocal(attacker, exp: -1, shoots: 1);

            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerDisconnect>((@event, _) =>
        {
            var player = @event.Userid;
            var entityIndex = player.Index;

            _userRankReset[entityIndex] = null;

            if (_users.TryGetValue(player.SteamID, out var user))
            {
                var steamId = new SteamID(player.SteamID);
                var totalTime = GetTotalTime(entityIndex);
                _loginTime[entityIndex] = DateTime.MinValue;

                Task.Run(() => UpdateUserStatsDb(steamId, user, totalTime, DateTimeOffset.Now.ToUnixTimeSeconds()));
                _users.Remove(player.SteamID);
            }

            return HookResult.Continue;
        });

        AddCommandListener("say", CommandListener_Say);
        AddCommandListener("say_team", CommandListener_Say);

        AddTimer(1.0f, () =>
        {
            if (!_config.EnableScoreBoardRanks) return;
            foreach (var player in Utilities.GetPlayers().Where(player => player.IsValid))
            {
                if (!_users.TryGetValue(player.SteamID, out var user)) continue;

                player.Clan = $"[{Regex.Replace(GetLevelFromExperience(user.value).Name, @"\{[A-Za-z]+}", "")}]";
            }
        }, TimerFlags.REPEAT);
        AddTimer(300.0f, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(u => u.IsValid))
            {
                if (!_users.TryGetValue(player.SteamID, out var user)) continue;

                var entityIndex = player.Index;
                var steamId = new SteamID(player.SteamID);
                var totalTime = GetTotalTime(entityIndex);

                Task.Run(() => UpdateUserStatsDb(steamId, user, totalTime, DateTimeOffset.Now.ToUnixTimeSeconds()));
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
        SteamID? steamId = null;

        Server.NextFrame(() => steamId = new SteamID(player.SteamID));
        if (steamId == null) return;
        var steamId2 = ReplaceFirstCharacter(steamId.SteamId2, '1');

        await ResetPlayerData(steamId2);
        Server.NextFrame(() =>
        {
            _users[steamId.SteamId64] = new User
            {
                steam = steamId2,
                name = player.PlayerName
            };
            _loginTime[player.Index] = DateTime.Now;
        });
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

    private async Task OnClientAuthorizedAsync(string playerName, SteamID steamId)
    {
        var userExists = await UserExists(steamId.SteamId2);

        if (!userExists)
            await AddUserToDb(playerName, steamId.SteamId2);

        var user = await GetUserStatsFromDb(steamId.SteamId2);

        if (user == null) return;

        _users[steamId.SteamId64] = new User
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

                    UpdateUserStatsLocal(attacker, "XP per kill", exp: configEvent.Kills, kills: 1);

                    if (@event.Penetrated > 0)
                        UpdateUserStatsLocal(attacker, "XP for killing through the wall",
                            exp: additionally.Penetrated);
                    if (@event.Thrusmoke)
                        UpdateUserStatsLocal(attacker, "XP for murder through smoke", exp: additionally.Thrusmoke);
                    if (@event.Noscope)
                        UpdateUserStatsLocal(attacker, "XP for murder without a scope", exp: additionally.Noscope);
                    if (@event.Headshot)
                        UpdateUserStatsLocal(attacker, "XP for a murder to the head", exp: additionally.Headshot);
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
        int shoots = 0, int hits = 0, int headshots = 0, int roundwin = 0, int roundlose = 0)
    {
        if (_config.MinPlayers > PlayersCount() ||
            Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!
                .WarmupPeriod) return;

        if (!_users.TryGetValue(player.SteamID, out var user)) return;

        user.name = player.PlayerName;

        exp = exp == -1 ? 0 : exp;

        if (increase)
            user.value += exp;
        else
            user.value -= exp;

        user.kills += kills;
        user.deaths += death;
        user.assists += assist;
        user.round_lose += roundlose;
        user.round_win += roundwin;
        user.headshots += headshots;
        user.hits += hits;
        user.shoots += shoots;

        if (user.value <= 0) user.value = 0;

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

        var currentExperience = user.value;

        foreach (var rank in _config.Ranks.OrderBy(pair => pair.Value))
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
                    SendMessageToSpecificChat(player, isUpRank
                        ? $"Congratulations! You've reached level \x06[ {newLevelName} \x06]"
                        : $"Oh no! Your level has decreased to \x02[ {newLevelName} \x02]");

                    if (_config.EnableScoreBoardRanks)
                        player.Clan = $"[{Regex.Replace(newLevel.Name, @"\{[A-Za-z]+}", "")}]";

                    user.rank = newLevel.Level;
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

    [ConsoleCommand("css_rank")]
    public void OnCmdRank(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null) return;

        var steamId = new SteamID(controller.SteamID);
        Task.Run(() => GetUserStats(controller, steamId));
    }

    private async Task GetUserStats(CCSPlayerController controller, SteamID steamId)
    {
        if (!_users.TryGetValue(steamId.SteamId64, out var user)) return;

        var totalTime = TimeSpan.FromSeconds(user.playtime);
        //var formattedTime = totalPlayTime.ToString(@"hh\:mm\:ss");
        var formattedTime =
            $"{(totalTime.Days >= 0 ? $"\x04{totalTime.Days}\x08 Days, " : "")}" +
            $"{(totalTime.Hours > 0 ? $"\x04{totalTime.Hours}\x08 Hours, " : "")}" +
            $"{(totalTime.Minutes > 0 ? $"\x04{totalTime.Minutes}\x08 Minutes, " : "")}" +
            $"{(totalTime.Seconds > 0 ? $"\x04{totalTime.Seconds}\x08 Seconds" : "")}";
        //var currentPlayTime = (DateTime.Now - _loginTime[index]).ToString(@"hh\:mm\:ss");
        var getPlayerTop = await GetPlayerRankAndTotal(steamId.SteamId2);

        Server.NextFrame(() =>
        {
            if (!controller.IsValid) return;

            var headshotPercentage =
                (double)user.headshots / (user.kills + 1) * 100;
            double kdr = 0;
            if (user is { kills: > 0, deaths: > 0 })
                kdr = (double)user.kills / (user.deaths + 1);

            SendMessageToSpecificChat(controller,
                "-------------------------------------------------------------------");
            if (getPlayerTop != null)
                SendMessageToSpecificChat(controller,
                    $" \x08 Your position in the top: \x0C#{getPlayerTop.PlayerRank}/{getPlayerTop.TotalPlayers}");

            SendMessageToSpecificChat(controller,
                $" \x08 Experience: \x04{user.value}\x08 (Rank:\x04 {ReplaceColorTags(GetLevelFromExperience(user.value).Name)}\x08)");
            SendMessageToSpecificChat(controller,
                $" \x08 Kills: \x04{user.kills} \x08(Headshot:\x04 {user.headshots}\x08) | Deaths: \x04{user.deaths} \x08| Assists: \x04{user.assists}");
            SendMessageToSpecificChat(controller,
                $" \x08 Winning rounds: \x04{user.round_win} \x08| Losing rounds: \x04{user.round_lose}");
            SendMessageToSpecificChat(controller,
                $" \x08 Percentage Headshot: \x04{headshotPercentage:0.00} \x08| KD: \x04{kdr:0.00}");
            SendMessageToSpecificChat(controller,
                $" \x08 Total Play Time: {formattedTime}");
            SendMessageToSpecificChat(controller,
                "-------------------------------------------------------------------");
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

            var steamId = new SteamID(players.SteamID);
            var totalTime = GetTotalTime(entityIndex);
            Task.Run(() => UpdateUserStatsDb(steamId, user, totalTime, DateTimeOffset.Now.ToUnixTimeSeconds()));
        }

        AddTimer(.5f, () => Task.Run(() => ShowTopPlayers(controller)));
    }

    private async Task ShowTopPlayers(CCSPlayerController controller)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var query = $@"
        SELECT DISTINCT * FROM `{_config.TableName}` ORDER BY `value` DESC LIMIT 10;";

            var topPlayers = await connection.QueryAsync<User>(query);

            Server.NextFrame(() => controller.PrintToChat("[\x0C Top Players\x01 ]"));
            var rank = 1;
            foreach (var player in topPlayers)
            {
                Server.NextFrame(() =>
                {
                    if (!controller.IsValid) return;

                    controller.PrintToChat(
                        $"{rank ++}. {ChatColors.Blue}{player.name} \x01[{ChatColors.Olive}{ReplaceColorTags(GetLevelFromExperience(player.value).Name)}\x01] -\x06 Experience: {ChatColors.Blue}{player.value}");
                });
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private HookResult EventRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        UpdateUserStatsLocal(@event.Userid, $"XP for the MVP of this round",
            exp: _config.Events.EventRoundMvp);

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
                            UpdateUserStatsLocal(player, "XP for losing a round", exp: configEvent.Loser, roundlose: 1,
                                increase: false);
                        else
                            UpdateUserStatsLocal(player, "XP for winning the round", exp: configEvent.Winner,
                                roundwin: 1);
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

    private async Task UpdateUserStatsDb(SteamID steamId, User user, TimeSpan playtime, long lastconnect)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var updateQuery = $@"
        UPDATE 
            `{_config.TableName}` 
        SET 
            `steam` = @SteamId,
            `name` = @Username,
            `value` = @Experience,
            `rank` = @LastLevel,
            `kills` = @Kills,
            `deaths` = @Deaths,
            `shoots` = @Shoots,
            `hits` = @Hits,
            `headshots` = @Headshots,
            `assists` = @Assists,
            `round_win` = @Roundwin,
            `round_lose` = @Roundlose,
            `playtime` = `playtime` + @PlayTime,
            `lastconnect` = @LastConnect
        WHERE 
            `steam` = @SteamId";

            await connection.ExecuteAsync(updateQuery, new
            {
                SteamId = ReplaceFirstCharacter(steamId.SteamId2, '1'),
                Username = user.name,
                LastLevel = user.rank,
                Experience = user.value,
                Kills = user.kills,
                Deaths = user.deaths,
                Shoots = user.shoots,
                Hits = user.hits,
                Headshots = user.headshots,
                Assists = user.assists,
                Roundwin = user.round_win,
                Roundlose = user.round_lose,
                PlayTime = playtime.TotalSeconds,
                LastConnect = lastconnect
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task<PlayerStats?> GetPlayerRankAndTotal(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var rankQuery =
                $"SELECT COUNT(*) + 1 AS PlayerRank FROM {_config.TableName} WHERE value > (SELECT value FROM {_config.TableName} WHERE steam = @SteamId);";

            var totalPlayersQuery = $"SELECT COUNT(*) AS TotalPlayers FROM {_config.TableName};";

            var playerRank =
                await connection.QueryFirstOrDefaultAsync<int>(rankQuery,
                    new { SteamId = ReplaceFirstCharacter(steamId, '1') });
            var totalPlayers = await connection.QueryFirstOrDefaultAsync<int>(totalPlayersQuery);

            return new PlayerStats { PlayerRank = playerRank, TotalPlayers = totalPlayers };
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    private async Task AddUserToDb(string playerName, string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var parameters = new User
            {
                steam = ReplaceFirstCharacter(steamId, '1'),
                name = playerName,
                value = 0,
                rank = 0,
                kills = 0,
                deaths = 0,
                shoots = 0,
                hits = 0,
                headshots = 0,
                assists = 0,
                round_win = 0,
                round_lose = 0,
                playtime = 0,
                lastconnect = 0,
            };

            var query = $@"
                INSERT INTO `{_config.TableName}` 
                (`steam`, `name`, `value`, `rank`, `kills`, `deaths`, `shoots`, `hits`, `headshots`, `assists`, `round_win`, `round_lose`, `playtime`, `lastconnect`) 
                VALUES 
                (@steam, @name, @value, @rank, @kills, @deaths, @shoots, @hits, @headshots, @assists, @round_win, @round_lose, @playtime, @lastconnect);";

            await connection.ExecuteAsync(query, parameters);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task ResetPlayerData(string steamId)
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            dbConnection.Open();

            var resetPlayerQuery = $@"
                UPDATE {_config.TableName}
                SET
                    value = 0,
                    rank = 0,
                    kills = 0,
                    deaths = 0,
                    shoots = 0,
                    hits = 0,
                    headshots = 0,
                    assists = 0,
                    round_win = 0,
                    round_lose = 0,
                    playtime = 0,
                    lastconnect = {DateTimeOffset.Now.ToUnixTimeSeconds()},
                WHERE steam = @SteamId;";

            await dbConnection.ExecuteAsync(resetPlayerQuery, new { SteamId = ReplaceFirstCharacter(steamId, '1') });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task CreateTable()
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            dbConnection.Open();

            var createLrTable = $@"
            CREATE TABLE IF NOT EXISTS `{_config.TableName}` (
                `steam` VARCHAR(255) NOT NULL,
                `name` VARCHAR(255) NOT NULL,
                `value` BIGINT NOT NULL,
                `rank` BIGINT NOT NULL,
                `kills` BIGINT NOT NULL,
                `deaths` BIGINT NOT NULL,
                `shoots` BIGINT NOT NULL,
                `hits` BIGINT NOT NULL,
                `headshots` BIGINT NOT NULL,
                `assists` BIGINT NOT NULL,
                `round_win` BIGINT NOT NULL,
                `round_lose` BIGINT NOT NULL,
                `playtime` BIGINT NOT NULL,
                `lastconnect` BIGINT NOT NULL
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

        return totalTime;
    }

    private async Task<User?> GetUserStatsFromDb(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();
            var user = await connection.QueryFirstOrDefaultAsync<User>(
                $"SELECT * FROM `{_config.TableName}` WHERE `steam` = @SteamId",
                new { SteamId = ReplaceFirstCharacter(steamId, '1') });

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
            TableName = "lvl_base",
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

    private async Task<bool> UserExists(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var exists = await connection.ExecuteScalarAsync<bool>(
                $"SELECT EXISTS(SELECT 1 FROM `{_config.TableName}` WHERE `steam` = @SteamId)",
                new { SteamId = ReplaceFirstCharacter(steamId, '1') });

            return exists;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return false;
    }

    private static string ReplaceFirstCharacter(string input, char replacement)
    {
        if (input.Length <= 0) return input;

        var charArray = input.ToCharArray();
        charArray[6] = replacement;

        return new string(charArray);
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
            "{DEFAULT}", "{WHITE}", "{DARKRED}", "{GREEN}", "{LIGHTYELLOW}", "{LIGHTBLUE}", "{OLIVE}", "{LIME}",
            "{RED}", "{LIGHTPURPLE}", "{PURPLE}", "{GREY}", "{YELLOW}", "{GOLD}", "{SILVER}", "{BLUE}", "{DARKBLUE}",
            "{BLUEGREY}", "{MAGENTA}", "{LIGHTRED}", "{ORANGE}"
        };

        string[] colorReplacements =
        {
            $"{ChatColors.Default}", $"{ChatColors.White}", $"{ChatColors.Darkred}", $"{ChatColors.Green}",
            $"{ChatColors.LightYellow}", $"{ChatColors.LightBlue}", $"{ChatColors.Olive}", $"{ChatColors.Lime}",
            $"{ChatColors.Red}", $"{ChatColors.LightPurple}", $"{ChatColors.Purple}", $"{ChatColors.Grey}",
            $"{ChatColors.Yellow}", $"{ChatColors.Gold}", $"{ChatColors.Silver}", $"{ChatColors.Blue}",
            $"{ChatColors.DarkBlue}", $"{ChatColors.BlueGrey}", $"{ChatColors.Magenta}", $"{ChatColors.LightRed}",
            $"{ChatColors.Orange}"
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
    public required string TableName { get; init; }
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