using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace DnsServer
{
    public class DnsCache
    {
        [JsonProperty]
        private ConcurrentDictionary<(string, int), (List<DnsAnswer>, TimeSpan)> answersCache;
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsCache()
        {
            answersCache=new ConcurrentDictionary<(string, int), (List<DnsAnswer>, TimeSpan)>();
        }

        public bool TryGetAnswer(string query, AnswerType type, out List<DnsAnswer> answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            if (answersCache.TryGetValue((query, (int)type), out var result))
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
                PutAnswer(answer.Name,answer.AnswerType,answer);
        }

        public void PutAnswer(string query, AnswerType type, DnsAnswer answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            var ttlTimeSpan=TimeSpan.FromSeconds(answer.TTL);
            var expirationTime = currentTime + ttlTimeSpan;
            if (answersCache.ContainsKey((query, (int)type)))
            {
                if (answersCache[(query, (int)type)].Item2 > expirationTime)
                {
                    var oldItem = answersCache[(query, (int)type)];
                    var newItem = (oldItem.Item1, expirationTime);
                    answersCache[(query, (int)type)] = newItem;
                }
                answersCache[(query, (int)type)].Item1.Add(answer);
            }
            else
            {
                answersCache[(query, (int)type)] = (new List<DnsAnswer> {answer},expirationTime);
            }
        }

        public async Task CleanExpiredAnswers()
        {
            logger.Info("GC started");
            var toDelete = answersCache
                .Where(pair => pair.Value.Item1.Any() &&
                               pair.Value.Item2<DateTimeExtensions.UtcNow())
                .Select(p => p.Key)
                .ToList();

            foreach (var query in toDelete)
            {
                answersCache.TryRemove(query, out var answer);
                logger.Info("Query {0} {1} removed from cache", query.Item1, query.Item2);
            }
        }
    }

    public struct CacheKey
    {
        public string Key { get; set; }
        public AnswerType AnswerType { get; set; }
    }
}