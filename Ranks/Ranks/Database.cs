using System.Reflection.Metadata;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using MySqlConnector;

namespace Ranks;

public class Database
{
    private readonly Ranks _ranks;
    private readonly string _dbConnectionString;

    public Database(Ranks ranks, string connection)
    {
        _ranks = ranks;
        _dbConnectionString = connection;
    }

    public async Task UpdateUserStatsDb(SteamID steamId, User user, TimeSpan playtime, long lastconnect)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var updateQuery = $@"
        UPDATE 
            `{_ranks.Config.TableName}` 
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
                SteamId = steamId.SteamId2.ReplaceFirstCharacter(),
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

    public async Task<PlayerStats?> GetPlayerRankAndTotal(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var rankQuery =
                $"SELECT COUNT(*) + 1 AS PlayerRank FROM {_ranks.Config.TableName} WHERE value > (SELECT value FROM {_ranks.Config.TableName} WHERE steam = @SteamId);";

            var totalPlayersQuery = $"SELECT COUNT(*) AS TotalPlayers FROM {_ranks.Config.TableName};";

            var playerRank =
                await connection.QueryFirstOrDefaultAsync<int>(rankQuery,
                    new
                    {
                        SteamId = steamId.ReplaceFirstCharacter(),
                    });
            var totalPlayers = await connection.QueryFirstOrDefaultAsync<int>(totalPlayersQuery);

            return new PlayerStats { PlayerRank = playerRank, TotalPlayers = totalPlayers };
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }


    public async Task AddUserToDb(string playerName, string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var parameters = new User
            {
                steam = steamId.ReplaceFirstCharacter(),
                name = playerName,
                value = _ranks.Config.InitialExperiencePoints,
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
                INSERT INTO `{_ranks.Config.TableName}` 
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

    public async Task ResetPlayerData(string steamId)
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            await dbConnection.OpenAsync();

            var resetPlayerQuery = $@"
                UPDATE {_ranks.Config.TableName}
                SET
                    value = @DefaultValue,
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
                    lastconnect = @LastConnect
                WHERE steam = @SteamId;";

            await dbConnection.ExecuteAsync(resetPlayerQuery, new
            {
                SteamId = steamId.ReplaceFirstCharacter(),
                DefaultValue = _ranks.Config.InitialExperiencePoints,
                LastConnect = DateTimeOffset.Now.ToUnixTimeSeconds()
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task CreateTable()
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            dbConnection.Open();

            var createLrTable = $@"
            CREATE TABLE IF NOT EXISTS `{_ranks.Config.TableName}` (
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

    public async Task<User?> GetUserStatsFromDb(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();
            var user = await connection.QueryFirstOrDefaultAsync<User>(
                $"SELECT * FROM `{_ranks.Config.TableName}` WHERE `steam` = @SteamId",
                new { SteamId = steamId.ReplaceFirstCharacter() });

            return user;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null;
    }
    
    public async Task<List<User>?> GetTopPlayers()
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            await dbConnection.OpenAsync();

            var selectTopPlayersQuery = $"SELECT * FROM {_ranks.Config.TableName} ORDER BY value DESC LIMIT 10;";

            var topPlayers = await dbConnection.QueryAsync<User>(selectTopPlayersQuery);

            return topPlayers.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null;
    }

    public async Task<bool> UserExists(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var exists = await connection.ExecuteScalarAsync<bool>(
                $"SELECT EXISTS(SELECT 1 FROM `{_ranks.Config.TableName}` WHERE `steam` = @SteamId)",
                new { SteamId = steamId.ReplaceFirstCharacter() });

            return exists;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return false;
    }

}

public class RankDb
{
    public required string Host { get; init; }
    public required string Database { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public int Port { get; init; }
}