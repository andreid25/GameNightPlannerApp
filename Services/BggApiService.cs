using System.Xml.Linq;
using Polly.RateLimiting;

public class BggApiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    public Dictionary<int, Game> games = new();

    public BggApiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Bgg:ApiKey"]
            ?? throw new Exception("BGG API key not configured");
    }

    public async Task<List<Game>>? GetGameAsync(string ids)
    {
        string url = $"https://boardgamegeek.com/xmlapi2/thing?id={ids}&stats=1";
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
        List<Game> games = new();
        foreach (XElement item in doc.Descendants("item"))
        {
            games.Add(new Game {
                GameId = int.Parse(item.Attribute("id")?.Value ?? "0"),
                Name = item.Descendants("name").FirstOrDefault(e => (string)e.Attribute("type") == "primary")?.Attribute("value")?.Value ?? "Unknown",
                ImageLink = item.Element("image")?.Value ?? "Unknown",
                MinPlayers = int.Parse(item.Element("minplayers")?.Attribute("value")?.Value ?? "0"),
                MaxPlayers = int.Parse(item.Element("maxplayers")?.Attribute("value")?.Value ?? "0"),
                PlayingTime = int.Parse(item.Element("playingtime")?.Attribute("value")?.Value ?? "0"),
                MinPlayTime = int.Parse(item.Element("minplaytime")?.Attribute("value")?.Value ?? "0"),
                MaxPlayTime = int.Parse(item.Element("maxplaytime")?.Attribute("value")?.Value ?? "0")
            });
        }
        return games;
    }

    public async Task<List<Game>> GetGameDetails(List<int> ids, CancellationToken ct)
    {
        List<Game> detailedCollection = new();
        foreach (int[] idGroup in ids.Chunk(20))
        {
            string idParam = string.Join(",", idGroup);
            List<Game> details = await GetGameAsync(idParam);
            if (details is not null)
            {
                detailedCollection.AddRange(details);
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

    public async Task<List<Game>> GetMergedCollectionsAsync(
        IEnumerable<string> usernames,
        CancellationToken ct = default)
    {
        Dictionary<int, HashSet<string>> gameOwners = new();
        foreach (string username in usernames)
        {
            List<CollectionItem> collectionItems = await GetUserCollectionAsync(username);

            foreach (CollectionItem collectionItem in collectionItems)
            {
                if (!gameOwners.TryGetValue(collectionItem.GameId, out var owners))
                {
                    owners = new HashSet<string>();
                    gameOwners[collectionItem.GameId] = owners;
                }

                owners.Add(username);
            }
        }

        List<Game> games = await GetGameDetails(gameOwners.Keys.ToList(), ct);

        foreach (Game game in games)
        {
            if (gameOwners.TryGetValue(game.GameId, out var owners))
            {
                game.Owners = owners;
            }
        }

        return games;
    }
}