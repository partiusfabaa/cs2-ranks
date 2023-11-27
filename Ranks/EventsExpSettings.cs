namespace Ranks;

public class EventsExpSettings
{
    public int EventRoundMvp { get; init; }
    public PlayerDeath EventPlayerDeath { get; init; } = null!;
    public Bomb EventPlayerBomb { get; init; } = null!;
    public RoundEnd EventRoundEnd { get; set; } = null!;
}

public class PlayerDeath
{
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; set; }
    public int KillingAnAlly { get; set; }
}

public class Bomb
{
    public int DroppedBomb { get; init; }
    public int PlantedBomb { get; init;}
    public int DefusedBomb { get; init; }
    public int PickUpBomb { get; init; }
}

public class RoundEnd
{
    public int Winner { get; set; }
    public int Loser { get; set; }
}