namespace Guardian.Core.Scheduling;

/// <summary>A window of time on given days. End &lt; Start means the window crosses midnight.</summary>
public sealed record TimeWindow(DayOfWeek[] Days, TimeOnly Start, TimeOnly End)
{
    public bool Contains(DateTime local)
    {
        var t = TimeOnly.FromDateTime(local);
        if (Start == End) return Days.Contains(local.DayOfWeek); // whole day
        if (Start < End) return Days.Contains(local.DayOfWeek) && t >= Start && t < End;
        // Overnight: e.g. Sun 22:00 -> Mon 07:00. Day refers to the day the window starts.
        if (t >= Start) return Days.Contains(local.DayOfWeek);
        if (t < End) return Days.Contains(local.AddDays(-1).DayOfWeek);
        return false;
    }
}

/// <summary>Set of weekly windows. An empty schedule means "always".</summary>
public sealed class WeeklySchedule
{
    public List<TimeWindow> Windows { get; set; } = new();

    public static WeeklySchedule Always => new();

    public bool IsActive(DateTime local) => Windows.Count == 0 || Windows.Any(w => w.Contains(local));
}
