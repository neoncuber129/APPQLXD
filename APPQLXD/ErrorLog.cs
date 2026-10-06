using System.IO;

namespace APPQLXD;

public sealed class ErrorEntry
{
    public required DateTime Time { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
    public required string Detail { get; init; }
    public string TimeText => Time.ToString("HH:mm:ss dd/MM/yyyy");
}

public static class ErrorLog
{
    private static readonly object Gate = new();
    private static readonly List<ErrorEntry> Entries = [];
    private static int _depth;

    public static event Action<ErrorEntry>? EntryAdded;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "APPQLXD",
        "error.log");

    public static void Record(Exception exception, string source)
    {
        if (Interlocked.Increment(ref _depth) > 4)
        {
            Interlocked.Decrement(ref _depth);
            return;
        }

        var entry = new ErrorEntry
        {
            Time = DateTime.Now,
            Source = source,
            Message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message,
            Detail = exception.ToString()
        };
        var text = $"[{entry.Time:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{entry.Detail}";
        try
        {
            lock (Gate)
                Entries.Add(entry);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, text + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                /* vẫn hiện trên màn hình nếu không ghi được tệp */
            }

            EntryAdded?.Invoke(entry);
        }
        finally
        {
            Interlocked.Decrement(ref _depth);
        }
    }

    public static IReadOnlyList<ErrorEntry> Snapshot()
    {
        lock (Gate)
            return Entries.ToArray();
    }

    public static void Clear()
    {
        lock (Gate)
            Entries.Clear();
    }
}
