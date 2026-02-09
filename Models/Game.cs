public class Game
{
    public int GameId { get; set; }
    public string Name { get; set; } = "";
    public int YearPublished { get; set; }
    public string ImageLink { get; set; } = "";

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int PlayingTime { get; set; }
    public int MinPlayTime { get; set; }
    public int MaxPlayTime { get; set; }
}