using CounterStrikeSharp.API.Core;

namespace LvlCoreApi;

public interface ILvlCoreApi
{
    public Func<CCSPlayerController, int, int> PlayerGainedExperience { get; set; }
    public Func<CCSPlayerController, int, int> PlayerLostExperience { get; set; }
    public int GetExperiencePlayer(CCSPlayerController player);
    public void SetExperiencePlayer(CCSPlayerController player, int exp);
    public void TakeExperiencePlayer(CCSPlayerController player, int exp);
    public void GiveExperiencePlayer(CCSPlayerController player, int exp);
}