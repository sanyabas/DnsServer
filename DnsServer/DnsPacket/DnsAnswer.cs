﻿namespace DnsServer
{
    public class DnsAnswer
    {
        public string Name { get; set; }
        public AnswerType AnswerType { get; set; }
        public Class Class { get; set; }
        public uint TTL { get; set; }
        public string Data { get; set; }

        protected bool Equals(DnsAnswer other)
        {
            return string.Equals(Name, other.Name) && AnswerType == other.AnswerType;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((DnsAnswer) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ (int) AnswerType;
            }
        }
    }
}