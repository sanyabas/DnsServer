using System.Collections.Generic;

namespace DnsServer
{
    public class DnsPacket
    {
        public ushort QueryId { get; set; }
        public DnsFlags Flags { get; set; }
        public List<DnsQuery> Queries { get; set; }
        public List<DnsAnswer> Answers { get; set; }
        public List<DnsAnswer> AuthorityAnswers { get; set; }
        public List<DnsAnswer> AdditionalAnswers { get; set; }
    }
}