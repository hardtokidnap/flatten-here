namespace FlattenHere;

internal sealed class Logger : IDisposable
{
    private readonly StreamWriter _writer;
    public string Path { get; }
    public bool UsingFallback { get; }

    private Logger(StreamWriter writer, string path, bool usingFallback)
    {
        _writer = writer;
        Path = path;
        UsingFallback = usingFallback;
    }

    public static Logger Open(string targetFolder)
    {
        var preferred = System.IO.Path.Combine(targetFolder, "flatten-here.log");
        try
        {
            return OpenAt(preferred, fallback: false);
        }
        catch
        {
            var fallback = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"flatten-here-{Environment.ProcessId}.log");
            return OpenAt(fallback, fallback: true);
        }
    }

    private static Logger OpenAt(string path, bool fallback)
    {
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var sw = new StreamWriter(fs) { AutoFlush = true };
        return new Logger(sw, path, fallback);
    }

    public void Write(string verb, string detail)
        => _writer.WriteLine($"{DateTime.UtcNow:O}  {verb,-14} {detail}");

    public void WriteFail(string action, string src, string dst, Exception ex)
        => Write("FAIL", $"{action}  {src} -> {dst}  {ex.GetType().Name}: {ex.Message}");

    public void Dispose() => _writer.Dispose();
}
