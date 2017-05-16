using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                    //TODO evrth
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

    public class DnsCache
    {
        public ConcurrentDictionary<(string, Type), (string, TimeSpan)> DomainToIp { get; set; }
        public ConcurrentDictionary<(string, Type), (string, TimeSpan)> IpToDomain { get; set; }
        private ConcurrentDictionary<(string, Type), (List<DnsAnswer>, TimeSpan)> answersCache;
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsCache()
        {
            DomainToIp = new ConcurrentDictionary<(string, Type), (string, TimeSpan)>();
            IpToDomain = new ConcurrentDictionary<(string, Type), (string, TimeSpan)>();
            answersCache=new ConcurrentDictionary<(string, Type), (List<DnsAnswer>, TimeSpan)>();
        }

        public bool TryGetIp(string domain, Type type, out string ip)
        {
            if (TryGetValue(DomainToIp, domain, type, out var value))
            {
                ip = value;
                return true;
            }
            ip = null;
            return false;
        }

        public bool TryGetDomain(string ip, Type type, out string domain)
        {
            if (TryGetValue(IpToDomain, ip, type, out var value))
            {
                domain = value;
                return true;
            }
            domain = null;
            return false;
        }

        public bool TryGetAnswer(string query, Type type, out List<DnsAnswer> answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            if (answersCache.TryGetValue((query, type), out var result))
            {
                if (currentTime < result.Item2)
                {
                    answer = result.Item1;
                    return true;
                }
            }
            answer = null;
            return false;
        }

        public void PutAnswers(IEnumerable<DnsAnswer> answers)
        {
            foreach (var answer in answers)
                PutAnswer(answer.Name,answer.Type,answer);
        }

        public void PutAnswer(string query, Type type, DnsAnswer answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            var ttlTimeSpan=TimeSpan.FromSeconds(answer.TTL);
            var expirationTime = currentTime + ttlTimeSpan;
            if (answersCache.ContainsKey((query, type)))
            {
                if (answersCache[(query, type)].Item2 > expirationTime)
                {
                    var oldItem = answersCache[(query, type)];
                    var newItem = (oldItem.Item1, expirationTime);
                    answersCache[(query, type)] = newItem;
                }
                answersCache[(query, type)].Item1.Add(answer);
            }
            else
            {
                answersCache[(query, type)] = (new List<DnsAnswer> {answer},expirationTime);
            }
        }

        private bool TryGetValue(IDictionary<(string, Type), (string, TimeSpan)> dictionary, string key,
            Type type, out string value)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            if (dictionary.TryGetValue((key, type), out var result))
            {
                if (currentTime < result.Item2)
                {
                    value = result.Item1;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public void PutIpToDomain(string ip, Type type, string domain,uint ttl)
        {
            PutValue(IpToDomain,ip,type,domain,ttl);
        }

        public void PutDomainToIp(string domain, Type type, string ip, uint ttl)
        {
            PutValue(DomainToIp,domain,type,ip,ttl);
        }

        private void PutValue(IDictionary<(string, Type), (string, TimeSpan)> dictionary, string key, Type type,
            string value, uint ttl)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            var ttlTimeSpan=TimeSpan.FromSeconds(ttl);
            var expirationTime = currentTime + ttlTimeSpan;
            dictionary[(key, type)] = (value, expirationTime);
        }

        public async Task CleanExpiredAnswers()
        {
            logger.Info("GC started");
            //var toDelete = new List<DnsQuery>();
            var toDelete = answersCache
                .Where(pair => pair.Value.Item1.Any() &&
                               pair.Value.Item1.Min(a => a.TTL) + pair.Value.Item2.TotalSeconds <
                               DateTimeExtensions.UtcNow().TotalSeconds)
                .Select(p => p.Key)
                .ToList();

            foreach (var query in toDelete)
            {
//                (DnsPacket, TimeSpan) answer;
                answersCache.TryRemove(query, out var answer);
                logger.Info("Query {0} {1} removed from cache", query.Item1, query.Item2);
            }
        }
    }
}