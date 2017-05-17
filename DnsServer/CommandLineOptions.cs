using CommandLine;

namespace DnsServer
{
    public class CommandLineOptions
    {
        [Option('s', "remote server", DefaultValue = "8.8.8.8")]
        public string Server { get; set; }

        [Option('f', "cache filename", DefaultValue = "cache.json")]
        public string CacheFileName { get; set; }
    }
}