using System.Text;
using System.Text.Json;
using ScanTrackNode.Models;

namespace ScanTrackNode.Services;

public class PackageForwarder
{
    private readonly NodeRegistry _registry;
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<PackageForwarder> _logger;

    // Hur länge vi väntar på nästa nod innan vi ger upp. 10 sekunder
    // Utan timeout kan en hängd nod låsa vår request i minuter.
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(10);

    public PackageForwarder(NodeRegistry registry, IHttpClientFactory factory, ILogger<PackageForwarder> logger)
    {
        _registry = registry;
        _factory = factory;
        _logger = logger;
    }

    // Vidarebefordrar paketet till nästa nod i nätverket.
    // Returnerar true om nästa nod svarade med 2xx, annars false.
    public async Task<bool> ForwardAsync(Package package, string nextCity)
    {
        // 1. Slå upp nästa stads adress i det centrala registret
        var nodes = await _registry.GetNodesAsync();

        if (!nodes.TryGetValue(nextCity, out var url))
        {
            _logger.LogError(
                "Kunde inte vidarebefordra {Id}: {NextCity} finns inte i registret",
                package.PackageId, nextCity);
            return false;
        }

        // 2. Serialisera paketet (historiken är redan uppdaterad av controllern)
        var json = JsonSerializer.Serialize(package);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 3. Skicka med timeout
        var http = _factory.CreateClient();
        http.Timeout = ForwardTimeout;

        try
        {
            var response = await http.PostAsync($"{url.TrimEnd('/')}/paket", content);

            _logger.LogInformation(
                "Paket {Id} skickat till {NextCity} ({Url}) — svar {StatusCode}",
                package.PackageId, nextCity, url, (int)response.StatusCode);

            return response.IsSuccessStatusCode;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError(
                "Timeout ({Timeout}s) när {Id} skickades till {NextCity} ({Url})",
                ForwardTimeout.TotalSeconds, package.PackageId, nextCity, url);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Nätverksfel när {Id} skickades till {NextCity} ({Url})",
                package.PackageId, nextCity, url);
            return false;
        }
    }
}
