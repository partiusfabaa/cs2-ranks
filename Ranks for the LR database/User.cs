namespace Ranks;

public class User
{
    public required string steam { get; set; }
    public required string name { get; set; }
    public int value { get; set; } //exp
    public int rank { get; set; }
    public int kills { get; set; }
    public int deaths { get; set; }
    public int shoots { get; set; }
    public int hits { get; set; }
    public int headshots { get; set; }
    public int assists { get; set; }
    public int round_win { get; set; }
    public int round_lose { get; set; }
    public long playtime { get; set; }
    public long lastconnect { get; set; }
}