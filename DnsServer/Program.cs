using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DnsServer
{
    class Program
    {
        //TODO logging
        static void Main(string[] args)
        {
            Run();
            //var t = new Task(async () =>
            //{
            //    using (var udpClient = new UdpClient(31337))
            //    {
            //        string loggingEvent = "";
            //        while (true)
            //        {
            //            Console.WriteLine("start");
            //            //IPEndPoint object will allow us to read datagrams sent from any source.
            //            var receivedResults = await udpClient.ReceiveAsync();
            //            loggingEvent += Encoding.ASCII.GetString(receivedResults.Buffer);
            //            Console.WriteLine(loggingEvent);
            //        }
            //    }
            //},TaskCreationOptions.AttachedToParent);
            //t.Start();

            //t.Wait();
        }

        static void Run()
        {
            var server=new DnsServer();
            using (var listener = new UdpClient(53))
            {
                Console.WriteLine("start listening");
                listener.StartProcessingRequestsAsync(CreateAsyncCallback(server)).Wait();
            }
        }

        static Func<UdpReceiveResult, Task<byte[]>> CreateAsyncCallback(DnsServer server)
        {
            return async result =>
            {
                var answer= await server.HandleQuery(result.Buffer);
                return DnsPacketParser.CreatePacket(answer);
            };
        }
    }

    public class BigEndianBinaryReader : BinaryReader
    {
        public BigEndianBinaryReader(Stream input) : base(input)
        {
        }

        public BigEndianBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        public BigEndianBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
        {
        }

        public override ushort ReadUInt16()
        {
            var a16 = base.ReadBytes(2);
            Array.Reverse(a16);
            return BitConverter.ToUInt16(a16, 0);
        }

        public override uint ReadUInt32()
        {
            var a32 = ReadBytes(4);
            Array.Reverse(a32);
            return BitConverter.ToUInt32(a32, 0);
        }
    }
}