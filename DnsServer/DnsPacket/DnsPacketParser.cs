using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace DnsServer
{
    public class DnsPacketParser
    {
        public static DnsPacket ParsePacket(byte[] packet)
        {
            using (var stream = new MemoryStream(packet))
            {
                using (var reader = new BigEndianBinaryReader(stream, Encoding.BigEndianUnicode))
                {
                    var id = reader.ReadUInt16();
                    var flagsByte = reader.ReadUInt16();
                    var flags = DnsFlags.ParseFlags(flagsByte);
                    var questionsCount = reader.ReadUInt16();
                    var answersCount = reader.ReadUInt16();
                    var authorityAnswersCount = reader.ReadUInt16();
                    var additionalAnswersCount = reader.ReadUInt16();
                    var nameCache = new Dictionary<int, byte[]>();
                    var questions = new List<DnsQuery>();
                    var answers = new List<DnsAnswer>();
                    var additionalAnswers = new List<DnsAnswer>();
                    var authoritativeAnswers = new List<DnsAnswer>();
                    for (var i = 0; i < questionsCount; i++)
                        questions.Add(ParseQuery(reader, packet, (int)reader.BaseStream.Position, nameCache));
                    for (var i = 0; i < answersCount; i++)
                        answers.Add(ParseAnswer(reader, packet, (int)reader.BaseStream.Position, nameCache));
                    for (var i = 0; i < authorityAnswersCount; i++)
                        authoritativeAnswers.Add(ParseAnswer(reader, packet, (int)reader.BaseStream.Position, nameCache));
                    for (var i = 0; i < additionalAnswersCount; i++)
                        additionalAnswers.Add(ParseAnswer(reader, packet, (int)reader.BaseStream.Position, nameCache));
                    var result = new DnsPacket
                    {
                        QueryId = id,
                        Flags = flags,
                        Queries = questions,
                        Answers = answers,
                        AuthorityAnswers = authoritativeAnswers,
                        AdditionalAnswers = additionalAnswers
                    };
                    return result;
                }
            }
        }

        private static DnsAnswer ParseAnswer(BinaryReader reader, byte[] packet, int position, Dictionary<int, byte[]> nameCache)
        {
            var (domain, shift) = ParseDomain(packet, position,nameCache);
            var _ = reader.BaseStream.Seek(shift, SeekOrigin.Begin);
            var type = (AnswerType) reader.ReadUInt16();
            var queryClass = (Class) reader.ReadUInt16();
            var ttl = reader.ReadUInt32();
            var dataLength = reader.ReadUInt16();
            var data = "";
            if (type == AnswerType.A)
            {
                var dataArray = new List<byte>();
                for (var i = 0; i < dataLength; i++)
                    dataArray.Add(reader.ReadByte());
                data = string.Join(".", dataArray.Select(item => item.ToString()));
            }
            else if (type == AnswerType.NS)
            {
                var (name, nsShift) = ParseDomain(packet, (int)reader.BaseStream.Position,nameCache);
                var __ = reader.BaseStream.Seek(nsShift, SeekOrigin.Begin);
                data = name;
            }
            else if (type == AnswerType.SOA)
            {
                data = "";
            }
            var answer = new DnsAnswer
            {
                Class = queryClass,
                AnswerType = type,
                Name = domain,
                TTL = ttl,
                Data = data
            };
            return answer;
        }

        public static DnsQuery ParseQuery(BinaryReader reader, byte[] query, int position, Dictionary<int, byte[]> cache = null)
        {
            var (domain, shift) = ParseDomain(query, position,cache);
            var _ = reader.BaseStream.Seek(shift, SeekOrigin.Begin);
            var type = (AnswerType) reader.ReadUInt16();
            var queryClass = (Class) reader.ReadUInt16();
            var result = new DnsQuery
            {
                Name = domain,
                Class = queryClass,
                AnswerType = type
            };
            return result;
        }

        private static (string, int) ParseDomain(byte[] query, int position, Dictionary<int, byte[]> cache)
        {
            var number = query[position];

            var resultArray = new byte[1024];
            var shift = 0;
            var cacheStart=0;
            while (number != 0)
            {
                if (number >= 0xc0)
                {

                    var cachePosition = ((number & 0b111111) << 8) + query[position + 1];
                    Array.Copy(cache[cachePosition],0,resultArray,shift,cache[cachePosition].Length);
                    position++;
                    break;
                }
                else
                {
                    if (cacheStart == 0)
                        cacheStart = position;
                    Array.Copy(query, position + 1, resultArray, shift, number);
                    position += number + 1;
                    shift += number;
                    resultArray[shift] = (byte)'.';
                    shift++;
                }
                number = query[position];
            }
            position++;
            resultArray = resultArray.TakeWhile(b => b != 0).ToArray();
            if (cacheStart!=0)
            for (var i = 0; i < resultArray.Length; i++)
            {
                if (resultArray[i] == 46 || i==0)
                {
                    var cacheShift = resultArray[i] == 46 ? 1 : 0;
                    var temp = new byte[512];
                    Array.Copy(resultArray,i+cacheShift,temp,0,resultArray.Length-i-cacheShift);
                    cache[cacheStart + i+cacheShift] = temp.TakeWhile(b => b != 0).ToArray();
                }
            }
            var result = Encoding.UTF8.GetString(resultArray.TakeWhile(b => b != 0).ToArray());
            return (result, position);
        }

        public static byte[] CreatePacket(DnsPacket packet)
        {
            var result = new List<byte>();
            var queryBytes = BitConverter.GetBytes(packet.QueryId);
            Array.Reverse(queryBytes);
            result.AddRange(queryBytes);

            var flags = DnsFlags.CreateFlags(packet.Flags);
            var flagsByte = BitConverter.GetBytes(flags);
            Array.Reverse(flagsByte);
            result.AddRange(flagsByte);

            var questionsCount = BitConverter.GetBytes((short)packet.Queries.Count);
            Array.Reverse(questionsCount);
            result.AddRange(questionsCount);

            var answersCount = BitConverter.GetBytes((short)packet.Answers.Count);
            Array.Reverse(answersCount);
            result.AddRange(answersCount);

            var authoritativeCount = BitConverter.GetBytes((short)packet.AuthorityAnswers.Count);
            Array.Reverse(authoritativeCount);
            result.AddRange(authoritativeCount);

            var additionalCount = BitConverter.GetBytes((short)packet.AdditionalAnswers.Count);
            Array.Reverse(additionalCount);
            result.AddRange(additionalCount);

            var nameCache = new Dictionary<string, int>();
            foreach (var query in packet.Queries)
                result.AddRange(CreateQuery(query, result.Count, nameCache));
            foreach (var answer in packet.Answers)
                result.AddRange(CreateAnswer(answer, result.Count, nameCache));
            foreach (var answer in packet.AuthorityAnswers)
                result.AddRange(CreateAnswer(answer, result.Count, nameCache));
            foreach (var answer in packet.AdditionalAnswers)
                result.AddRange(CreateAnswer(answer, result.Count, nameCache));

            return result.ToArray();
        }

        public static byte[] CreateQuery(DnsQuery query, int position, Dictionary<string, int> cache)
        {
            var result = new List<byte>();
            var encodedDomain = EncodeDomain(query.Name, position, cache, false);
            var type = (short)query.AnswerType;
            var typeBytes = BitConverter.GetBytes(type);
            Array.Reverse(typeBytes);
            var classValue = (short)query.Class;
            var classBytes = BitConverter.GetBytes(classValue);
            Array.Reverse(classBytes);
            result.AddRange(encodedDomain);
            result.AddRange(typeBytes);
            result.AddRange(classBytes);
            return result.ToArray();
        }

        private static byte[] EncodeDomain(string domain, int position, Dictionary<string, int> cache, bool isData)
        {
            var result = new List<byte>();
            while (!string.IsNullOrEmpty(domain))
            {
                var shift = domain.IndexOf(".", StringComparison.Ordinal);
                if (cache.ContainsKey(domain))
                {
                    var number = (ushort)(0b11 << 14 | cache[domain]);
                    var bytes = BitConverter.GetBytes(number);
                    Array.Reverse(bytes);
                    result.AddRange(bytes);
                    return result.ToArray();
                }
                else
                {
                    var name = domain.Substring(0, shift);
                    result.Add((byte)name.Length);
                    var encodedName = Encoding.UTF8.GetBytes(name);
                    result.AddRange(encodedName);
                    cache[domain] = position+ (isData?2:0);
                    position += encodedName.Length + 1;
                    domain = domain.Substring(shift + 1);
                }
            }
            result.Add(0);
            return result.ToArray();
        }

        public static byte[] CreateAnswer(DnsAnswer answer, int position, Dictionary<string, int> cache)
        {
            var result = new List<byte>();
            var encodedDomain = EncodeDomain(answer.Name, position, cache, false);
            result.AddRange(encodedDomain);

            var type = (short)answer.AnswerType;
            var typeBytes = BitConverter.GetBytes(type);
            Array.Reverse(typeBytes);
            result.AddRange(typeBytes);

            var classValue = (short)answer.Class;
            var classBytes = BitConverter.GetBytes(classValue);
            Array.Reverse(classBytes);
            result.AddRange(classBytes);

            var ttlBytes = BitConverter.GetBytes(answer.TTL);
            Array.Reverse(ttlBytes);
            result.AddRange(ttlBytes);

            byte[] dataBytes;
            if (answer.AnswerType == AnswerType.A)
            {
                Console.WriteLine(answer.Data);
                dataBytes = IPAddress.Parse(answer.Data).GetAddressBytes();
            }
            else if (answer.AnswerType==AnswerType.NS)
                dataBytes = EncodeDomain(answer.Data, position + result.Count, cache, true);
            else
                dataBytes=new byte[0];
            var length = (ushort)dataBytes.Length;
            var lengthBytes = BitConverter.GetBytes(length);
            Array.Reverse(lengthBytes);
            result.AddRange(lengthBytes);
            result.AddRange(dataBytes);
            return result.ToArray();
        }

        public static DnsPacket CreateSimpleErrorPacket(DnsQuery query, ushort queryId,byte replyCode)
        {
            var flags = new DnsFlags
            {
                RecursionDesired = true,
                RecursionAvailable = true,
                ReplyCode = replyCode
            };
            var packet = new DnsPacket
            {
                QueryId = queryId,
                Flags = flags,
                Queries = new List<DnsQuery> {query},
                Answers = new List<DnsAnswer>(),
                AdditionalAnswers = new List<DnsAnswer>(),
                AuthorityAnswers = new List<DnsAnswer>(),
            };
            return packet;
        }
    }
}