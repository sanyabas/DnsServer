using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DnsServer
{
    class Program
    {
        static void Main(string[] args)
        {
//            Task.Run(() => Run());
            Task.Run(async () =>
            {
                using (var udpClient = new UdpClient(31337))
                {
                    string loggingEvent = "";
                    while (true)
                    {
                        //IPEndPoint object will allow us to read datagrams sent from any source.
                        var receivedResults = await udpClient.ReceiveAsync();
                        loggingEvent += Encoding.ASCII.GetString(receivedResults.Buffer);
                    }
                }
            });
        }

        static void Run()
        {
            using (var listener = new UdpClient(31337))
            {
                Console.WriteLine("start listening");
                listener.StartProcessingRequestsAsync(CreateAsyncCallback());
            }
        }

        static Func<UdpReceiveResult, Task> CreateAsyncCallback()
        {
            return async result =>
            {
                File.WriteAllText("recv.txt", result.Buffer.ToString());
            };
        }
    }

    public static class UdpClientExtensions
    {
        public static async Task StartProcessingRequestsAsync(this UdpClient client,
            Func<UdpReceiveResult, Task> callback)
        {
            while (true)
            {
                try
                {
                    var endPoint = new IPEndPoint(IPAddress.Any, 0);
                    Console.WriteLine("try");
                    var query = await client.ReceiveAsync();
                    Console.WriteLine("get");
                    await callback(query);
                }
                catch (Exception e)
                {
                    throw new NotImplementedException();
                }
            }
        }
    }

    public class DnsPacket
    {
        public int QueryId { get; }
        public DnsFlags Flags { get; set; }
        public List<DnsQuery> Queries { get; set; }
        public List<DnsAnswer> Answers { get; set; }
        public List<DnsAnswer> AuthorityAnswers { get; set; }
        public List<DnsAnswer> AdditionalAnswers { get; set; }
    }

    public class DnsFlags
    {
        public bool Response { get; set; }
        public Opcode Opcode { get; set; }
        public bool Truncated { get; set; }
        public bool Recursion { get; set; }
        public bool AdBit { get; set; }
        public bool NonAuthenticated { get; set; }
    }

    public enum Opcode
    {
        Standard
    }

    public class DnsQuery
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public Class Class { get; set; }
    }

    public class DnsAnswer
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public Class Class { get; set; }
        public int TTL { get; set; }
        public string Data { get; set; }
    }

    public enum Type
    {
        A,
        NS,
        MX
    }

    public enum Class
    {
        IN
    }
}