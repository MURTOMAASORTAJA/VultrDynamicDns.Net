using CommandLine;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VultrDynamicDns.Net.Models;

internal class Program
{
    private static readonly CancellationTokenSource CancelTokenSource = new();
    private static readonly HttpClientHandler Handler = CreateHandler();
    private static readonly HttpClient IpCheckClient = new HttpClient();
    private static UpdateOptions Options { get; set; } = new();
    public static readonly string CacheFilePath = Path.GetFullPath("dynamic-ip.cache");

    public static void Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<UpdateOptions>(args).MapResult(Work, errs => 1);
        Environment.Exit(result);
    }

    private static HttpClientHandler CreateHandler()
    {
        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseCookies = false
        };
    }

    private static HttpClient CreateVultrClient()
    {
        var client = new HttpClient(Handler);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Options.ApiKey}");
        client.DefaultRequestHeaders.ExpectContinue = false;
        return client;
    }

    static Situation GetSituationAndCache()
    {
        var cached = GetCachedIP(CacheFilePath);
        var current = GetCurrentIP();
        if (cached == null || (cached.ToString() != current.ToString()))
        {
            CacheIP(current, CacheFilePath);
        }
        return new(current, cached);
    }

    static int Work(UpdateOptions options)
    {
        Options = options;
        var vultrClient = CreateVultrClient();
        TimeSpan checkInterval = new(0, 10, 0);
        DateTime? latestCheck = null;
        while (!CancelTokenSource.IsCancellationRequested)
        {
            if (DateTime.UtcNow - latestCheck >= checkInterval || latestCheck == null)
            {
                latestCheck = DateTime.UtcNow;
                var situation = GetSituationAndCache();
                if (HasChanged(situation))
                {
                    ConsoleWriteIfVerbose($"IP address has changed to {situation.Current}\n");
                    UpdateDomain(vultrClient, situation.Current);
                }
            }
        }
        return 0;
    }

    static int UpdateDomain(HttpClient client, IPAddress newIp)
    {
        
        var domainStr = Options.Subdomain == null ? Options.Domain : $"{Options.Subdomain}.{Options.Domain}";
        ConsoleWriteIfVerbose("Getting list of records...");
        var listRecordsResponse = client.GetFromJsonAsync<ListRecordsResponse>($"https://api.vultr.com/v2/domains/{Options.Domain}/records", CancelTokenSource.Token).Result;
        ConsoleWriteIfVerbose($" done. {listRecordsResponse!.Records.Length}\n", false);

        ConsoleWriteIfVerbose($"Looking up the record for {Options.Domain}...");
        var record = Options.Subdomain == null
            ? listRecordsResponse!.Records.FirstOrDefault(r => r.Name == "" && r.Type == "A")
            : listRecordsResponse!.Records.FirstOrDefault(r => r.Name == Options.Subdomain && r.Type == "A");
        
        if (record == null)
        {
            ConsoleWriteIfVerbose(" not found.\n", false);
            ConsoleWriteIfVerbose("Creating new record...");
            var createRecordResponse = client.PostAsJsonAsync($"https://api.vultr.com/v2/domains/{Options.Domain}/records", new
            {
                name = Options.Subdomain,
                type = "A",
                data = newIp.ToString(),
                ttl = 300
            }, CancelTokenSource.Token).Result;

            if (!createRecordResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"\nFailed to create a new record to Vultr: {createRecordResponse.StatusCode}");
                return 1;
            }

            var response = createRecordResponse.Content.ReadFromJsonAsync<CreateRecordResponse>(JsonSerializerOptions.Default, CancelTokenSource.Token).Result;

            if (response == null)
            {
                Console.WriteLine($"\nCan't update: the response to creating a new record to Vultr was empty.");
                return 1;
            }
            record = response.Record;
            ConsoleWriteIfVerbose($" done. ({record.Id})\n", false);
        }

        if (record.Data != newIp.ToString())
        {
            ConsoleWriteIfVerbose($" found. ({record.Id})\n", false);
            ConsoleWriteIfVerbose("Updating record...");
            var updateRecordResponse = client.PatchAsJsonAsync($"https://api.vultr.com/v2/domains/{Options.Domain}/records/{record.Id}", new
            {
                data = newIp.ToString()
            }, CancelTokenSource.Token).Result;
            if (!updateRecordResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"\nFailed to update the record to Vultr: {updateRecordResponse.StatusCode}");
                return 1;
            }
            ConsoleWriteIfVerbose($" done.\n", false);
        } else
        {
            ConsoleWriteIfVerbose(" found. (no update needed)\n", false);
        }
        return 0;
    }

    static void CacheIP(IPAddress ip, string cacheFilePath) => File.WriteAllText(cacheFilePath, ip.ToString());
    static IPAddress? GetCachedIP(string cacheFilePath) => File.Exists(cacheFilePath) ? (IPAddress.TryParse(File.ReadAllText(cacheFilePath), out var ip) ? ip : null) : null;
    static IPAddress GetCurrentIP() => IPAddress.Parse(IpCheckClient.GetStringAsync("https://api.ipify.org", CancelTokenSource.Token).Result);
    static bool HasChanged(Situation situation) => situation.Current != situation.Previous;
    static void ConsoleWriteIfVerbose(string text, bool timestampPrefix = true)
    {
        if (Options.Verbose)
        {
            if (timestampPrefix)
            {
                Console.Write($"[{DateTime.UtcNow}]");
                if (!string.IsNullOrEmpty(text))
                {
                    Console.Write(' ');
                }
            }
            Console.Write(text);
        }
    }
}