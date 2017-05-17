using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using DnsServer.Helpers;

namespace DnsServer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var options = new CommandLineOptions();
                if (!Parser.Default.ParseArguments(args, options))
                    return;
                Run(options);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void Run(CommandLineOptions options)
        {
            using (var server = new DnsServer(options.CacheFileName, options.Server))
            {
                using (var listener = new UdpClient(new IPEndPoint(IPAddress.Any, 53)))
                {
                    var cts = new CancellationTokenSource();
                    Console.CancelKeyPress += (sender, args) =>
                    {
                        cts.Cancel();
                        args.Cancel = true;
                    };
                    try
                    {
                        listener.Client.ReceiveTimeout = 3;
                        listener.StartProcessingRequestsAsync(CreateAsyncCallback(server), server.Dispose)
                            .Wait(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }

        private static Func<UdpReceiveResult, Task<byte[]>> CreateAsyncCallback(DnsServer server)
        {
            return async result =>
            {
                var answer = await server.HandleQuery(result.Buffer);
                return DnsPacketParser.CreatePacket(answer);
            };
        }
    }
}