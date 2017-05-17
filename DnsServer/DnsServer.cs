using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json;

namespace DnsServer
{
    public static class DateTimeExtensions
    {
        public static TimeSpan UtcNow()
        {
            return DateTime.UtcNow - new DateTime(1970, 1, 1);
        }
    }

    public class DnsServer : IDisposable
    {
        public DnsCache AnswersCache { get; set; }
        private static IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("212.193.163.6"), 53);
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsServer()
        {
            AnswersCache = new DnsCache();
        }

        private async Task SaveToFile(FileInfo file)
        {
            throw new NotImplementedException();
        }



        public async Task<DnsPacket> HandleQuery(byte[] buffer)
        {
            await AnswersCache.CleanExpiredAnswers();
            var parsedPacket = DnsPacketParser.ParsePacket(buffer);
            var queries = parsedPacket.Queries;
            var answers = await Task.WhenAll(queries.AsParallel()
                .Select(async query =>
                {
                    logger.Info("Handling query {0} {1} {2}", query.Name, query.Type, query.Class);
                    (DnsPacket, TimeSpan) answer;
                    DnsPacket resultAnswer;
//                    bool answerInCache;
//                    lock (AnswersCache)
//                        answerInCache = AnswersCache.TryGetValue(query, out answer);
                    var answerInCache = AnswersCache.TryGetAnswer(query.Name, query.Type, out var dnsAnswers);
                    if (!answerInCache)
                    {
                        try
                        {
//                            answer.Item1 = await ResolveQuery(buffer);
                            resultAnswer=await ResolveQuery(buffer);
                        }
                        catch (TimeoutException e)
                        {
                            logger.Error(e, "Query time out: {0} {1}", query.Name, query.Type);
                            return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId, 2);
                        }
                        logger.Info("Query {0} {1}, TTL:{2}", query.Name, query.Type,
                            (resultAnswer.Answers.Any() ? resultAnswer.Answers.Min(a => a.TTL) : 0));
                        if (resultAnswer.Flags.ReplyCode != 0)
                            return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId, 5);

//                        AnswersCache[query] = (answer.Item1, DateTimeExtensions.UtcNow());
                        PutAsnwersInCache(resultAnswer);
                    }
                    else
                    {
                        logger.Info("Query <{0} {1} {2}> found in cache!", query.Name, query.Class, query.Type);
//                        answer.Item1.QueryId = parsedPacket.QueryId;
                        parsedPacket.Answers.AddRange(dnsAnswers);
                        resultAnswer = parsedPacket;
                        resultAnswer.Flags.Response = true;
                        resultAnswer.Flags.RecursionAvailable = true;
                    }
                    return resultAnswer;
                }));
            return answers[0];
        }

        private void PutAsnwersInCache(DnsPacket packet)
        {
            AnswersCache.PutAnswers(packet.Answers);
            AnswersCache.PutAnswers(packet.AuthorityAnswers);
            AnswersCache.PutAnswers(packet.AdditionalAnswers);
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
                var sendRes = await Task.WhenAny(sendTask, Task.Delay(1500));
                int sent;
                if (sendRes == sendTask)
                    sent = await sendTask;
                logger.Info("sent to server");
                var receiveTask = udpClient.ReceiveAsync();
                var res = await Task.WhenAny(receiveTask, Task.Delay(1500));
                UdpReceiveResult result = new UdpReceiveResult();
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
                logger.Info("Received answer for <{0} {1} {2}>", parsedAnswer.Queries[0].Name,
                    parsedAnswer.Queries[0].Type, parsedAnswer.Queries[0].Class);
                return parsedAnswer;
            }
        }

        public void Dispose()
        {
            var str = JsonConvert.SerializeObject(AnswersCache);
            logger.Info("Server stopping, saving cache to disk");
            File.WriteAllText("cache.json", str);
        }
    }
}