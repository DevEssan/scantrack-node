namespace ScanTrackNode.Graph;

public static class GraphLoader
{
    // Hämtar cities.csv från registret vid uppstart.
    // Faller tillbaka på lokal fil om registret inte svarar.
    public static async Task<Dictionary<string, List<(string City, int Km)>>> LoadFromRegistryOrFileAsync(
        string registryUrl, string fallbackPath)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var csv = await http.GetStringAsync($"{registryUrl}/cities.csv");
            return ParseCsv(csv.Split('\n'));
        }
        catch
        {
            return Load(fallbackPath);
        }
    }

    // Läser cities.csv och bygger en dubbelriktad viktad graf.
    // Format: from,to,km (rubrikrad ignoreras)
    public static Dictionary<string, List<(string City, int Km)>> Load(string csvPath) =>
        ParseCsv(File.ReadLines(csvPath).Skip(1));

    private static Dictionary<string, List<(string City, int Km)>> ParseCsv(IEnumerable<string> lines)
    {
        var graph = new Dictionary<string, List<(string, int)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            var from = parts[0].Trim();
            var to = parts[1].Trim();

            if (!int.TryParse(parts[2].Trim(), out var km)) continue;

            graph.TryAdd(from, new List<(string, int)>());
            graph.TryAdd(to, new List<(string, int)>());

            graph[from].Add((to, km));
            graph[to].Add((from, km));
        }

        return graph;
    }
}
