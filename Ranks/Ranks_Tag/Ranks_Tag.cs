using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using Dapper;
using MySqlConnector;
using RanksApi;

namespace Ranks_Tag;

public class RanksTag : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "[Ranks] Tag";
    public override string ModuleVersion => "v1.0.0";

    private IRanksApi? _api;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = IRanksApi.Capability.Get();
        if (_api == null) return;

        AddTimer(5f, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(u => u.IsValid))
            {
                var level = _api.GetLevelFromExperience(_api.GetPlayerExperience(player));
                
                player.Clan = $"[{Regex.Replace(level.Name, @"\{[A-Za-z]+}", "")}]";
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
            }
        }, TimerFlags.REPEAT);
    }
}