using System;

namespace DnsServer.Helpers
{
    public static class DateTimeExtensions
    {
        public static TimeSpan UtcNow()
        {
            return DateTime.UtcNow - new DateTime(1970, 1, 1);
        }
    }
}