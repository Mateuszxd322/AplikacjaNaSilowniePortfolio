using System;

namespace AplikacjaNaSiłownię.Helpers
{
    public static class TimeHelper
    {
        public static DateTime GetPolishTime()
        {
            TimeZoneInfo zone;
            try
            {
                // Standardowy identyfikator dla systemu Windows
                zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback dla systemów Linux/macOS/Docker
                zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
            }
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        }
    }
}
