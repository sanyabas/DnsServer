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

    public static class DateTimeExtensions
    {
        public static TimeSpan UtcNow()
        {
            return DateTime.UtcNow - new DateTime(1970, 1, 1);
        }
    }
    public class DnsServer
    {
        public static ConcurrentDictionary<DnsQuery, (DnsPacket, TimeSpan)> AnswersCache { get; set; }
        private static IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsServer()
        {
            AnswersCache=new ConcurrentDictionary<DnsQuery, (DnsPacket,TimeSpan)>();
        }

        private async Task SaveToFile(FileInfo file)
        {
            throw new NotImplementedException();
        }

        public async Task CleanExpiredAnswers()
        {
                logger.Info("GC started");
                //var toDelete = new List<DnsQuery>();
            var toDelete = AnswersCache
                .Where(pair => pair.Value.Item1.Answers.Any() &&
                               pair.Value.Item1.Answers.Min(a => a.TTL) + pair.Value.Item2.TotalSeconds <
                               DateTimeExtensions.UtcNow().TotalSeconds)
                .Select(p => p.Key)
                .ToList();

                foreach (var query in toDelete)
                {
                    (DnsPacket, TimeSpan) answer;
                    AnswersCache.TryRemove(query,out answer);
                    logger.Info("Query {0} {1} removed from cache",query.Name,query.Type);
                }
        }

        public async Task<DnsPacket> HandleQuery(byte[] buffer)
        {
            await CleanExpiredAnswers();
            var parsedPacket = DnsPacketParser.ParsePacket(buffer);
            var queries = parsedPacket.Queries;
            var answers = await Task.WhenAll(queries.AsParallel().Select(async query =>
            {
                logger.Info("Handling query {0} {1} {2}",query.Name,query.Type,query.Class);
                //TODO evrth
                (DnsPacket, TimeSpan) answer;
                bool answerInCache;
                lock (AnswersCache)
                    answerInCache = AnswersCache.TryGetValue(query, out answer);
                if (!answerInCache || answer.Item2.TotalSeconds+answer.Item1.Answers.Min(a=>a.TTL)<DateTimeExtensions.UtcNow().TotalSeconds)
                {
                    try
                    {
                        answer.Item1 = await ResolveQuery(buffer);
                    }
                    catch (TimeoutException e)
                    {
                        logger.Error(e,"Query time out: {0} {1}",query.Name,query.Type);
                        return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId,2);
                    }
                    logger.Info("Query {0} {1}, TTL:{2}",query.Name,query.Type, (answer.Item1.Answers.Any()?answer.Item1.Answers.Min(a=>a.TTL):0));
                    if (answer.Item1.Flags.ReplyCode != 0)
                        return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId,5);
  
                        AnswersCache[query] = (answer.Item1, DateTimeExtensions.UtcNow());
                }
                else
                {
                    logger.Info("Query <{0} {1} {2}> found in cache!",query.Name,query.Class,query.Type);
                    answer.Item1.QueryId = parsedPacket.QueryId;
                }
                return answer.Item1;
            }));
            return answers[0];
        }

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
                var sendTask = udpClient.SendAsync(packet, packet.Length, remoteEndPoint);
                var sendRes= await Task.WhenAny(sendTask,Task.Delay(1500));
                int sent;
                if (sendRes == sendTask)
                    sent = await sendTask;
                logger.Info("sent to server");
                var receiveTask = udpClient.ReceiveAsync();
                var res= await Task.WhenAny(receiveTask, Task.Delay(1500));
                UdpReceiveResult result=new UdpReceiveResult();
                if (res == receiveTask)
                {
                    result = await receiveTask;
                }
                else
                    throw new TimeoutException("Query timed out");
                logger.Info("received from server");
                if (!Equals(result.RemoteEndPoint, remoteEndPoint))
                    throw new NotImplementedException();
                var parsedAnswer = DnsPacketParser.ParsePacket(result.Buffer);
                logger.Info("Received answer for <{0} {1} {2}>",parsedAnswer.Queries[0].Name, parsedAnswer.Queries[0].Type, parsedAnswer.Queries[0].Class);
                    return parsedAnswer;
            }
        }
    }
}