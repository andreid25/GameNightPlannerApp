using System.Xml.Linq;

public class BggApiService
{
    private readonly HttpClient _http;

    public BggApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<Game> GetGameAsync(int id)
    {
        var url = $"https://boardgamegeek.com/xmlapi2/thing?id={id}&stats=1";
        var xml = await _http.GetStringAsync(url);

        var doc = XDocument.Parse(xml);
        var item = doc.Descendants("item").First();

        return new Game
        {
            Id = id,
            Name = item.Elements("name")
                       .First(n => n.Attribute("type")?.Value == "primary")
                       .Attribute("value")!.Value,
            YearPublished = int.Parse(item.Element("yearpublished")?.Attribute("value")?.Value ?? "0"),
            AverageRating = double.Parse(item.Descendants("average").First().Attribute("value")!.Value),
            UsersRated = int.Parse(item.Descendants("usersrated").First().Attribute("value")!.Value)
        };
    }

    public async Task<List<Game>> GetGamesAsync(IEnumerable<int> ids)
    {
        var tasks = ids.Select(GetGameAsync);
        return (await Task.WhenAll(tasks)).ToList();
    }

    public async Task<List<CollectionItem>> GetUserCollectionAsync(string username)
    {
        string url = $"https://boardgamegeek.com/xmlapi2/collection?username={username}&own=1";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            string? xml = await _http.GetStringAsync(url);
            XDocument? doc = XDocument.Parse(xml);

            // TODO: Realistically we should check for the exact message
            if (doc.Root?.Name == "message")
            {
                await Task.Delay(2000);
                continue;
            }

            return doc.Descendants("item")
                .Select(item => new CollectionItem
                {
                    GameId = int.Parse(item.Attribute("objectid")!.Value),
                    Name = item.Element("name")?.Value ?? "Unknown",
                    YearPublished = int.Parse(
                        item.Element("yearpublished")?.Value ?? "0")
                })
                .ToList();
        }

        throw new Exception("BGG collection request timed out.");
    }
}