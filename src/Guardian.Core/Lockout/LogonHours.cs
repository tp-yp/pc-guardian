using Guardian.Core.Scheduling;

namespace Guardian.Core.Lockout;

/// <summary>
/// Builds the 21-byte Windows "logon hours" bitmap (168 bits, bit 0 = Sunday 00:00-01:00 UTC).
/// An hour is denied only if the lock is active for that entire hour; partial hours are
/// handled by the minute-level sign-out loop. This makes Windows itself refuse sign-in at the lock screen.
/// </summary>
public static class LogonHours
{
    public static byte[] Compute(WeeklySchedule locked, TimeZoneInfo zone, DateTime referenceUtc)
    {
        var bits = new byte[21];
        // Start from the Sunday 00:00 UTC of the reference week.
        var weekStart = referenceUtc.Date.AddDays(-(int)referenceUtc.DayOfWeek);
        for (var h = 0; h < 168; h++)
        {
            var hourStartUtc = DateTime.SpecifyKind(weekStart.AddHours(h), DateTimeKind.Utc);
            if (!LockedWholeHour(locked, zone, hourStartUtc)) bits[h / 8] |= (byte)(1 << (h % 8));
        }
        return bits;
    }

    public static bool IsAllowed(byte[] bits, int hourOfWeekUtc) => (bits[hourOfWeekUtc / 8] & (1 << (hourOfWeekUtc % 8))) != 0;

    private static bool LockedWholeHour(WeeklySchedule s, TimeZoneInfo zone, DateTime hourStartUtc)
    {
        if (s.Windows.Count == 0) return false;
        for (var m = 0; m < 60; m += 5)
            if (!s.IsActive(TimeZoneInfo.ConvertTimeFromUtc(hourStartUtc.AddMinutes(m), zone))) return false;
        return s.IsActive(TimeZoneInfo.ConvertTimeFromUtc(hourStartUtc.AddMinutes(59), zone));
    }
}
