using System.Text;

namespace Guardian.Service;

/// <summary>
/// Writes the service's own log to daily text files in GuardianData\logs (kept for 14 days),
/// so "export logs" in the management window has something to hand over when there is a problem.
/// Information and up from Geder itself, warnings and up from everything else.
/// </summary>
public sealed class FileLogProvider(string dir) : ILoggerProvider
{
    public static string Dir => Path.Combine(Paths.DataDir, "logs");
    private const int KeepDays = 14;
    private readonly object _gate = new();
    private string _day = "";

    public ILogger CreateLogger(string category) => new FileLogger(this, category);
    public void Dispose() { }

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
                .Append(level switch { LogLevel.Critical => "CRIT", LogLevel.Error => "ERR ", LogLevel.Warning => "WARN", LogLevel.Information => "INFO", _ => "DBG " })
                .Append(' ').Append(category[(category.LastIndexOf('.') + 1)..]).Append(": ").Append(message);
            if (ex != null) line.Append(Environment.NewLine).Append("    ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            lock (_gate)
            {
                Directory.CreateDirectory(dir);
                var day = now.ToString("yyyyMMdd");
                if (day != _day) { _day = day; Prune(); }
                File.AppendAllText(Path.Combine(dir, $"geder-{day}.log"), line.Append(Environment.NewLine).ToString());
            }
        }
        catch (Exception) { /* logging must never break the service */ }
    }

    private void Prune()
    {
        foreach (var f in Directory.GetFiles(dir, "geder-*.log"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays))
                try { File.Delete(f); } catch (Exception) { /* in use */ }
    }

    private sealed class FileLogger(FileLogProvider owner, string category) : ILogger
    {
        private readonly bool _own = category.StartsWith("Guardian", StringComparison.Ordinal);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= (_own ? LogLevel.Information : LogLevel.Warning);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) owner.Write(category, level, formatter(state, ex), ex);
        }
    }
}
