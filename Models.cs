using CommandLine;
using System.Net;

namespace VultrDynamicDns.Net.Models
{
    /// <summary>
    /// Contains the current and previous IP addresses.
    /// </summary>
    public record Situation(IPAddress Current, IPAddress? Previous);

    /// <summary>
    /// Contains the response from Vultr's DNS API
    /// </summary>
    public record ListRecordsResponse(Record[] Records);

    /// <summary>
    /// Contains the response from Vultr's DNS API.
    /// </summary>
    public record CreateRecordResponse(Record Record);

    /// <summary>
    /// Contains the record data from Vultr's DNS API.
    /// </summary>
    public record Record(string Id, string Type, string Name, string Data, int Priority, int Ttl);

    /// <summary>
    /// Contains the command line options for the update command.
    /// </summary>
    [Verb("update", aliases: new[] { "u" }, HelpText = "Update the IP address on Vultr")]
    public class UpdateOptions
    {
        [Option('k', "api-key", Required = true, HelpText = "Vultr API key")]
        public string ApiKey { get; set; } = "";

        [Option('d', "domain", Required = true, HelpText = "Domain to update")]
        public string Domain { get; set; } = "";

        [Option('s', "subdomain", Required = false, HelpText = "Subdomain to update (i.e. the name of the A record)")]
        public string Subdomain { get; set; } = "";

        [Option('v', "verbose", Required = false, HelpText = "Print verbose output")]
        public bool Verbose { get; set; } = false;
    }

}
