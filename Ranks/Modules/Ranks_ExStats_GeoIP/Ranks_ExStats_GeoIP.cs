using System.Net;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Dapper;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using RanksApi;

namespace Ranks_ExStats_GeoIP;

public class RanksExStatsGeoIp : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "[Ranks] ExStats GeoIP";
    public override string ModuleVersion => "v1.0.0";

    private IRanksApi? _api;
    private readonly PluginCapability<IRanksApi> _pluginCapability = new("ranks-core:api");

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = _pluginCapability.Get();
        if (_api == null) return;

        Task.Run(CreateTable);
        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player.IsBot) return;

            var ip = player.IpAddress;
            var steamId = ReplaceFirstCharacter(id.SteamId2);

            if (ip == null)
            {
                Logger.LogError("The IP address of the player {player} is null!", player.PlayerName);
                return;
            }
            
            Task.Run(() => OnClientAuthorizedAsync(steamId, ip.Split(':')[0]));
        });
    }

    private async Task CreateTable()
    {
        await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
        await connection.OpenAsync();

        var query = $"""
                     CREATE TABLE IF NOT EXISTS `{_api.DatabaseTableName}_geoip` (
                         `steam` varchar(32) NOT NULL,
                         `clientip` varchar(16) NOT NULL,
                         `country` varchar(48) NOT NULL,
                         `region` varchar(48) NOT NULL,
                         `city` varchar(48) NOT NULL,
                         `country_code` varchar(4) NOT NULL,
                         PRIMARY KEY (`steam`)
                     ) CHARSET=utf8;
                     """;

        await connection.ExecuteAsync(query);
    }

    private async Task OnClientAuthorizedAsync(string steamId, string ip)
    {
        try
        {
            using var reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-City.mmdb"));

            var response = reader.City(IPAddress.Parse(ip));

            var countryName = response.Country.Name ?? string.Empty;
            var cityName = response.City.Name ?? string.Empty;
            var regionName = response.MostSpecificSubdivision.Name ?? string.Empty;
            var countryCode = response.Country.IsoCode ?? string.Empty;

            await using var connection = new MySqlConnection(_api.DatabaseConnectionString);
            await connection.OpenAsync();

            var query = $"""
                             INSERT INTO `{_api.DatabaseTableName}_geoip` (steam, clientip, country, region, city, country_code)
                             VALUES (@Steam, @Ip, @Country, @Region, @City, @Code)
                             ON DUPLICATE KEY UPDATE
                                 clientip = @Ip,
                                 country = @Country,
                                 region = @Region,
                                 city = @City,
                                 country_code = @Code;
                         """;

            await connection.ExecuteAsync(query, new
            {
                Steam = steamId,
                Ip = ip,
                Country = countryName,
                Region = regionName,
                City = cityName,
                Code = countryCode
            });
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