namespace DnsServer
{
    public class DnsFlags
    {
        public bool Response { get; set; }
        public Opcode Opcode { get; set; }
        public bool Authoritative { get; set; }
        public bool Truncated { get; set; }
        public bool RecursionDesired { get; set; }
        public bool RecursionAvailable { get; set; }
        public bool AdBit { get; set; }
        public bool AnswerAuthenticated { get; set; }
        public bool NonAuthenticated { get; set; }
        public byte ReplyCode { get; set; }

        public static DnsFlags ParseFlags(ushort flags)
        {
            var replyCode = (byte) (flags & 0x000f);
            var nonAuthenticated = (flags & 0b1000) != 0;
            var answerAuthenticated = (flags & 0b10000) != 0;
            var recursionAvailable = (flags & 0b10000000) != 0;
            var recursionDesired = (flags & 0b100000000) != 0;
            var truncated = (flags & 0b1000000000) != 0;
            var authoritative = (flags & 0b10000000000) != 0;
            var opcodeByte = flags & 0b111100000000000;
            var opcode = Opcode.Standard;
            if (opcodeByte == 0)
                opcode = Opcode.Standard;
            var responce = (flags & 0b1000000000000000) != 0;

            var result = new DnsFlags
            {
                Response = responce,
                Opcode = opcode,
                Authoritative = authoritative,
                Truncated = truncated,
                RecursionDesired = recursionDesired,
                RecursionAvailable = recursionAvailable,
                AdBit = false,
                AnswerAuthenticated = answerAuthenticated,
                NonAuthenticated = nonAuthenticated,
                ReplyCode = replyCode
            };
            return result;
        }

        public static ushort CreateFlags(DnsFlags flags)
        {
            var response = BoolToBit(flags.Response);
            var opcode = (byte) flags.Opcode;
            var authoritative = BoolToBit(flags.Authoritative);
            var truncated = BoolToBit(flags.Truncated);
            var recursionDesired = BoolToBit(flags.RecursionDesired);
            var recursionAvailable = BoolToBit(flags.RecursionAvailable);
            var answerAuthenticated = BoolToBit(flags.AnswerAuthenticated);
            var nonAuthenticated = BoolToBit(flags.NonAuthenticated);
            var replyCode = flags.ReplyCode;

            var value = (response << 15) | ((opcode & 0b1111) << 11) | (authoritative << 10) | (truncated << 9) |
                        (recursionDesired << 8) | (recursionAvailable << 7) | (answerAuthenticated << 5) |
                        (nonAuthenticated << 4) | replyCode;
            return (ushort) value;
        }

        private static byte BoolToBit(bool value)
        {
            return (byte) (value ? 1 : 0);
        }
    }
}