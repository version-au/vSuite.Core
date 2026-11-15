using System;

namespace vSuite
{
    /// <summary>Times as the suite shows them: the short date and a 12-hour time ("3/11/2026 2:05 PM").</summary>
    public static class TimeText
    {
        public static string Stamp(DateTime d)
        {
            if (d == DateTime.MinValue) return "";
            return d.ToString("d") + " " + d.ToString("h:mm tt");
        }
    }
}
