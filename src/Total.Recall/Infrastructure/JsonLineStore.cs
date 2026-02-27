using System.Text.Json;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Generic JSONL (JSON Lines) file store. One JSON object per line.
/// Provides read-all, query, and append operations.
/// Caches data in memory and reloads only when the file changes on disk.
/// </summary>
public sealed class JsonLineStore<T> where T : class
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;
    private List<T>? _cache;
    private DateTime _cacheTimestamp;

    public JsonLineStore(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Load all records from the JSONL file. Returns empty list if file doesn't exist.
    /// Uses in-memory cache — only rereads from disk when file's LastWriteTimeUtc changes.
    /// </summary>
    public List<T> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            _cache = null;
            return [];
        }

        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        if (_cache is not null && lastWrite == _cacheTimestamp)
            return _cache;

        var results = new List<T>();
        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = JsonSerializer.Deserialize<T>(line, s_options);
            if (record is not null)
                results.Add(record);
        }

        _cache = results;
        _cacheTimestamp = lastWrite;
        return results;
    }

    /// <summary>
    /// Load and filter records using a predicate.
    /// </summary>
    public List<T> Query(Func<T, bool> predicate)
    {
        return LoadAll().Where(predicate).ToList();
    }

    /// <summary>
    /// Append a single record to the JSONL file.
    /// </summary>
    public void Append(T record)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var line = JsonSerializer.Serialize(record, s_options);
        File.AppendAllText(_filePath, line + Environment.NewLine);
    }

    /// <summary>
    /// Write all records, replacing the file contents.
    /// </summary>
    public void WriteAll(IEnumerable<T> records)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(_filePath, append: false);
        foreach (var record in records)
        {
            var line = JsonSerializer.Serialize(record, s_options);
            writer.WriteLine(line);
        }
    }

    /// <summary>
    /// Returns true if the JSONL file exists and has at least one record.
    /// </summary>
    public bool HasData()
    {
        return File.Exists(_filePath) && new FileInfo(_filePath).Length > 0;
    }

    /// <summary>
    /// Returns the number of records (lines) in the file.
    /// </summary>
    public int Count()
    {
        if (!File.Exists(_filePath))
            return 0;

        return File.ReadLines(_filePath).Count(l => !string.IsNullOrWhiteSpace(l));
    }
}
