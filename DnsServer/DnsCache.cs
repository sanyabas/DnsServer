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
        private ConcurrentDictionary<AnswerType, ConcurrentDictionary<string, (List<DnsAnswer>, TimeSpan)>> answersCache
            ;

        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsCache()
        {
            answersCache =
                new ConcurrentDictionary<AnswerType, ConcurrentDictionary<string, (List<DnsAnswer>, TimeSpan)>>();
            answersCache[AnswerType.A] = new ConcurrentDictionary<string, (List<DnsAnswer>, TimeSpan)>();
            answersCache[AnswerType.NS] = new ConcurrentDictionary<string, (List<DnsAnswer>, TimeSpan)>();
        }

        public bool TryGetAnswer(string query, AnswerType type, out List<DnsAnswer> answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            if (answersCache[type].TryGetValue(query, out var result))
                if (currentTime < result.Item2)
                {
                    answer = result.Item1;
                    return true;
                }
            answer = null;
            return false;
        }

        public void PutAnswers(IEnumerable<DnsAnswer> answers)
        {
            foreach (var answer in answers)
                PutAnswer(answer.Name, answer.AnswerType, answer);
        }

        public void PutAnswer(string query, AnswerType type, DnsAnswer answer)
        {
            var currentTime = DateTimeExtensions.UtcNow();
            var ttlTimeSpan = TimeSpan.FromSeconds(answer.TTL);
            var expirationTime = currentTime + ttlTimeSpan;
            if (answersCache[type].ContainsKey(query))
            {
                if (answersCache[type][query].Item2 > expirationTime)
                {
                    var oldItem = answersCache[type][query];
                    var newItem = (oldItem.Item1, expirationTime);
                    answersCache[type][query] = newItem;
                }
                answersCache[type][query].Item1.Add(answer);
            }
            else
            {
                answersCache[type][query] = (new List<DnsAnswer> {answer}, expirationTime);
            }
        }

        public async Task CleanExpiredAnswers()
        {
            logger.Info("GC started");
            var toDeleteA = answersCache[AnswerType.A]
                .Where(pair => pair.Value.Item1.Any() &&
                               pair.Value.Item2 < DateTimeExtensions.UtcNow())
                .Select(p => p.Key)
                .ToList();
            var toDeleteNs = answersCache[AnswerType.A]
                .Where(pair => pair.Value.Item1.Any() &&
                               pair.Value.Item2 < DateTimeExtensions.UtcNow())
                .Select(p => p.Key)
                .ToList();

            foreach (var query in toDeleteA)
            {
                answersCache[AnswerType.A].TryRemove(query, out var _);
                logger.Info("Query {0} removed from cache", query);
            }
            foreach (var query in toDeleteNs)
            {
                answersCache[AnswerType.NS].TryRemove(query, out var _);
                logger.Info("Query {0} removed from cache", query);
            }
        }
    }
}