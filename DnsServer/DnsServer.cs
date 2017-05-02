using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace DnsServer
{
    public class DnsServer
    {
        public static ConcurrentDictionary<DnsQuery, DnsPacket> AnswersCache { get; set; }
        private static IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsServer()
        {
            AnswersCache=new ConcurrentDictionary<DnsQuery, DnsPacket>();
        }

        private async Task SaveToFile(FileInfo file)
        {
            throw new NotImplementedException();
        }

        public async Task<DnsPacket> HandleQuery(byte[] buffer)
        {
            var parsedPacket = DnsPacketParser.ParsePacket(buffer);
            //DnsPacketParser.CreatePacket(parsedPacket);
            var queries = parsedPacket.Queries;
            var answers = await Task.WhenAll(queries.AsParallel().Select(async query =>
            {
                logger.Info("Handling query {0} {1} {2}",query.Name,query.Type,query.Class);
                //TODO evrth
                DnsPacket answer;
                bool answerInCache;
                lock (AnswersCache)
                    answerInCache = AnswersCache.TryGetValue(query, out answer);
                if (!answerInCache)
                {
                    answer = await ResolveQuery(buffer);
                    lock (AnswersCache)
                        AnswersCache[query] = answer;
                }
                return answer;
            }));
            return answers[0];
        }

        //private async Task SendAnswer()

        private async Task<DnsPacket> ResolveQuery(DnsQuery query)
        {
            using (var udpClient = new UdpClient())
            {
                var packet = new DnsPacket();
                var buffer = DnsPacketParser.CreatePacket(packet);
                await udpClient.SendAsync(buffer, buffer.Length, remoteEndPoint);
                var answer = await udpClient.ReceiveAsync();
                if (!Equals(answer.RemoteEndPoint, remoteEndPoint))
                    throw new NotImplementedException();
                var parsedAnswer = DnsPacketParser.ParsePacket(answer.Buffer);
                if (parsedAnswer.QueryId == packet.QueryId)
                    return parsedAnswer;
                throw new ArgumentException();
            }
        }

        private async Task<DnsPacket> ResolveQuery(byte[] packet)
        {
            logger.Info("Handling query");
            using (var udpClient = new UdpClient())
            {
                //var buffer = DnsPacketParser.CreatePacket(packet);
                var sendTask = udpClient.SendAsync(packet, packet.Length, remoteEndPoint);
                var sendRes= await Task.WhenAny(sendTask,Task.Delay(3000));
                int sent;
                if (sendRes == sendTask)
                    sent = await sendTask;
                logger.Info("sent to server");
                var receiveTask = udpClient.ReceiveAsync();
                var res= await Task.WhenAny(receiveTask, Task.Delay(3000));
                UdpReceiveResult result=new UdpReceiveResult();
                if (res == receiveTask)
                {
                    result = await receiveTask;

                }
                logger.Info("received from server");
                //var answer = await udpClient.ReceiveAsync();
                if (!Equals(result.RemoteEndPoint, remoteEndPoint))
                    throw new NotImplementedException();
                var parsedAnswer = DnsPacketParser.ParsePacket(result.Buffer);
                logger.Info("Received answer for <{0} {1} {2}>",parsedAnswer.Queries[0].Name, parsedAnswer.Queries[0].Type, parsedAnswer.Queries[0].Class);
                //if (parsedAnswer.QueryId == packet.QueryId)
                    return parsedAnswer;
                throw new ArgumentException();
            }
        }
    }
}