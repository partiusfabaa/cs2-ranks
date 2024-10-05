using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using RanksApi;

namespace Ranks_FakeRank;

[MinimumApiVersion(260)]
public class RanksFakeRank : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius";
    public override string ModuleName => "[Ranks] Fake Rank";
    public override string ModuleVersion => "v1.0.1";

    private Config _config;
    private IRanksApi? _api;
    private int _maxRank;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = IRanksApi.Capability.Get();
        if (_api == null) return;

        _config = _api.LoadConfig<Config>("ranks_fakerank");
        _maxRank = _config.FakeRank.Max(f => f.Key);
        RegisterListener<Listeners.OnTick>(() =>
        {
            var filter = new RecipientFilter();
            foreach (var player in Utilities.GetPlayers()
                         .Where(u => u is { IsValid: true, Connected: PlayerConnectedState.PlayerConnected, IsBot: false, IsHLTV: false }))
            {
                sbyte rankType;
                int rankValue;

                player.CompetitiveWins = 111;
                if (_config.Type is 0)
                {
                    rankType = 11;
                    rankValue = _api.GetPlayerExperience(player);
                }
                else
                {
                    rankType = 12;
                    var rank = _api.GetPlayerRank(player);

                    rankValue = _config.FakeRank[rank > _maxRank ? _maxRank : rank <= 0 ? 1 : rank];
                }

                player.CompetitiveRankType = rankType;
                player.CompetitiveRanking = rankValue;

                if (player is { IsValid: true, IsBot: false, IsHLTV: false })
                {
                    var buttons = player.Buttons;
                    if(buttons != 0 && buttons.ToString().Contains("858993"))
                        filter.Add(player);
                    
                }
            }

            if (filter.Count > 0)
            {
                var msg = UserMessage.FromId(350);
                msg.Send(filter);
            }
        });

        AddCommand("css_fakerank_reload", "", (player, info) =>
        {
            if (player != null) return;

            _config = _api.LoadConfig<Config>("ranks_fakerank");
            _maxRank = _config.FakeRank.Max(f => f.Key);
        });
    }
}

public class Config
{
    public int Type { get; set; } = 1;

    public Dictionary<int, int> FakeRank { get; set; } = new()
    {
        [1] = 1,
        [2] = 2,
        [3] = 3,
        [4] = 4,
        [5] = 5,
        [6] = 6,
        [7] = 7,
        [8] = 8,
        [9] = 9,
        [10] = 10,
        [11] = 11,
        [12] = 12,
        [13] = 13,
        [14] = 14,
        [15] = 15,
        [16] = 16,
        [17] = 17,
        [18] = 18,
    };
}