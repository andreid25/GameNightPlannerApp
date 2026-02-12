public class Filter
{
    //session lenth in minutes
    public List<Game> FilterByTime(List<Game> gamesToFilter, int sessionLength)
    {
        List<Game> filteredGames = gamesToFilter.Where(p => p.MaxPlayTime <= sessionLength).ToList();
        return filteredGames;
    }
}