namespace Ranks;

public class User
{
    public required string username { get; set; }
    public required string steamid { get; set; }
    public int experience { get; set; }
    public int score { get; set; }
    public int kills { get; set; }
    public int deaths { get; set; }
    public int assists { get; set; }
    public int noscope_kills { get; set; }
    public int damage { get; set; }
    public int mvp { get; set; }
    public int headshot_kills { get; set; }
    public double percentage_headshot { get; set; }
    public double kdr { get; set; }
    public DateTime last_active { get; set; }
    public int play_time { get; set; }
    public int last_level { get; set; }
}