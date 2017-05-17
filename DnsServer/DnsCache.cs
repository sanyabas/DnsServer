using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace DnsServer
{
    public class DnsCache
    {
        private ConcurrentDictionary<(string, Type), (List<DnsAnswer>, TimeSpan)> answersCache;
        private static Logger logger = LogManager.GetLogger("DnsServer");

        public DnsCache()
        {
            answersCache=new ConcurrentDictionary<(string, Type), (List<DnsAnswer>, TimeSpan)>();
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
}