using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace DnsServer
{
    public class DnsServer : IDisposable
    {
        public DnsCache AnswersCache { get; set; }
        private static IPEndPoint remoteEndPoint;
        private static readonly Logger Logger = LogManager.GetLogger("DnsServer");
        private bool cached;
        private readonly string cacheFilename;

        public DnsServer(string cacheFilename, string remoteServerAddress)
        {
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteServerAddress), 53);
            this.cacheFilename = cacheFilename;
            AnswersCache = new DnsCache();
            InitializeCache();
        }

        private void InitializeCache()
        {
            try
            {
                var cacheStr = File.ReadAllText(cacheFilename);
                AnswersCache = JsonConvert.DeserializeObject<DnsCache>(cacheStr);
                Logger.Info("Cache loaded");
            }
            catch (Exception e)
            {
                Logger.Error("Error while loading cache: {0}", e.Message);
                Logger.Info("Loading server without cache");
            }
        }

        public async Task<DnsPacket> HandleQuery(byte[] buffer)
        {
            await AnswersCache.CleanExpiredAnswers();
            var parsedPacket = DnsPacketParser.ParsePacket(buffer);
            var queries = parsedPacket.Queries;
            var answers = await Task.WhenAll(queries.AsParallel()
                .Select(async query =>
                {
                    Logger.Info("Handling query {0} {1} {2}", query.Name, query.AnswerType, query.Class);
                    DnsPacket resultAnswer;
                    var answerInCache = AnswersCache.TryGetAnswer(query.Name, query.AnswerType, out var dnsAnswers);
                    if (!answerInCache)
                    {
                        try
                        {
                            resultAnswer = await ResolveQuery(buffer);
                        }
                        catch (TimeoutException e)
                        {
                            Logger.Error(e, "Query time out: {0} {1}", query.Name, query.AnswerType);
                            return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId, 2);
                        }
                        Logger.Info("Query {0} {1}, TTL:{2}", query.Name, query.AnswerType,
                            resultAnswer.Answers.Any() ? resultAnswer.Answers.Min(a => a.TTL) : 0);
                        if (resultAnswer.Flags.ReplyCode != 0)
                            return DnsPacketParser.CreateSimpleErrorPacket(query, parsedPacket.QueryId, 5);

                        PutAsnwersInCache(resultAnswer);
                    }
                    else
                    {
                        Logger.Info("Query <{0} {1} {2}> found in cache!", query.Name, query.Class, query.AnswerType);
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

        private async Task<DnsPacket> ResolveQuery(byte[] packet)
        {
            Logger.Info("Handling query");
            using (var udpClient = new UdpClient())
            {
                var sendTask = udpClient.SendAsync(packet, packet.Length, remoteEndPoint);
                var sendRes = await Task.WhenAny(sendTask, Task.Delay(1500));
                if (sendRes == sendTask)
                    await sendTask;
                Logger.Info("sent to server");
                var receiveTask = udpClient.ReceiveAsync();
                var res = await Task.WhenAny(receiveTask, Task.Delay(1500));
                UdpReceiveResult result;
                if (res == receiveTask)
                    result = await receiveTask;
                else
                    throw new TimeoutException("Query timed out");
                Logger.Info("received from server");
                if (!Equals(result.RemoteEndPoint, remoteEndPoint))
                    throw new ArgumentException();
                var parsedAnswer = DnsPacketParser.ParsePacket(result.Buffer);
                Logger.Info("Received answer for <{0} {1} {2}>", parsedAnswer.Queries[0].Name,
                    parsedAnswer.Queries[0].AnswerType, parsedAnswer.Queries[0].Class);
                return parsedAnswer;
            }
        }

        public void Dispose()
        {
            lock (AnswersCache)
            {
                if (cached)
                    return;
                var str = JsonConvert.SerializeObject(AnswersCache);
                Logger.Info("Server stopping, saving cache to disk");
                File.WriteAllText(cacheFilename, str);
                Logger.Info("Successfully cached");
                cached = true;
            }
        }
    }
}