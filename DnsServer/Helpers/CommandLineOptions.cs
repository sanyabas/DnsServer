using CommandLine;
using CommandLine.Text;

namespace DnsServer
{
    public class CommandLineOptions
    {
        [Option('s', "server", HelpText = "Remote DNS server to route queries", DefaultValue = "8.8.8.8")]
        public string Server { get; set; }

        [Option('f', "filename", HelpText = "Path to cache file", DefaultValue = "cache.json")]
        public string CacheFileName { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("Caching DNS Server"),
                AdditionalNewLineAfterOption = true,
                AddDashesToOption = true
            };
            help.AddOptions(this);
            return help;
        }
    }
}