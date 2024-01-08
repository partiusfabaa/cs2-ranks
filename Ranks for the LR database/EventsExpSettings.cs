namespace Ranks;

public class EventsExpSettings
{
    public int EventRoundMvp { get; init; }
    public PlayerDeath EventPlayerDeath { get; init; } = null!;
    public Bomb EventPlayerBomb { get; init; } = null!;
    public RoundEnd EventRoundEnd { get; init; } = null!;
    public Additionally Additionally { get; init; } = null!;
}

public class PlayerDeath
{
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int KillingAnAlly { get; init; }
    public int Suicide { get; init; }
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
    public int Winner { get; init; }
    public int Loser { get; init; }
}

public class Additionally
{
    public int Headshot { get; init; }
    public int Noscope { get; init; }
    public int Attackerblind { get; init; }
    public int Thrusmoke { get; init; }
    public int Penetrated { get; init; }
}