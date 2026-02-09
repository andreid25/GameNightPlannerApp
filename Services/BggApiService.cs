using System.Threading;
using System.Xml.Linq;
using Polly.RateLimiting;

public class BggApiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public BggApiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Bgg:ApiKey"]
            ?? throw new Exception("BGG API key not configured");
    }

    public async Task<Game>? GetGameAsync(int id)
    {
        string url = $"https://boardgamegeek.com/xmlapi2/thing?id={id}&stats=1";
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        string? xml = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                xml = await _http.GetStringAsync(url);
                break;
            }
            catch (RateLimiterRejectedException e)
            {
                TimeSpan delay = e.RetryAfter ?? TimeSpan.FromSeconds(2);
                await Task.Delay(delay);
            }
        }
        if (xml is null)
        {
            return null;
        }
        XDocument doc = XDocument.Parse(xml);
        XElement item = doc.Descendants("item").First();

        return new Game
        {
            Name = item.Descendants("name").FirstOrDefault(e => (string)e.Attribute("type") == "primary").Attribute("value")?.Value ?? "Unknown",
            MinPlayers = int.Parse(
                item.Element("minplayers")?.Attribute("value")?.Value ?? "0"),
            MaxPlayers = int.Parse(
                item.Element("maxplayers")?.Attribute("value")?.Value ?? "0"),
            PlayingTime = int.Parse(
                item.Element("playingtime")?.Attribute("value")?.Value ?? "0"),
            MinPlayTime = int.Parse(
                item.Element("minplaytime")?.Attribute("value")?.Value ?? "0"),
            MaxPlayTime = int.Parse(
                item.Element("maxplaytime")?.Attribute("value")?.Value ?? "0")
        };
    }

    public async Task<List<Game>> GetUserCollectionWithDetailsAsync(string username)
    {
        List<CollectionItem> collection = await GetUserCollectionAsync(username);
        List<Game> detailedCollection = new();
        foreach (CollectionItem collectionItem in collection)
        {
            Game? details = await GetGameAsync(collectionItem.GameId);
            if (details is not null)
            {
                detailedCollection.Add(details);
            }
        }

        return detailedCollection;
    }

    public async Task<List<CollectionItem>> GetUserCollectionAsync(string username)
    {
        string url = $"https://boardgamegeek.com/xmlapi2/collection?username={username}&own=1&subtype=boardgame&excludesubtype=boardgameexpansion";
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string? xml = await _http.GetStringAsync(url);
            XDocument? doc = XDocument.Parse(xml);
            Console.WriteLine(doc);
            Console.WriteLine(doc.Root?.Name);
            Console.WriteLine("Hello");

            if (doc.Root?.Name == "message" && doc.Descendants("message").FirstOrDefault()?.Value == "Your request for this collection has been accepted and will be processed.  Please try again later for access.")
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