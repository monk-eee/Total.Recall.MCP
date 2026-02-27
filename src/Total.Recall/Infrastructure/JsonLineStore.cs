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
        var lineNum = 0;
        var errorCount = 0;
        foreach (var line in File.ReadLines(_filePath))
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonSerializer.Deserialize<T>(line, s_options);
                if (record is not null)
                    results.Add(record);
            }
            catch (JsonException ex)
            {
                errorCount++;
                if (errorCount <= 5) // log first 5 errors to avoid flooding
                {
                    Log.Error($"[JsonLineStore<{typeof(T).Name}>] corrupt line {lineNum} in {Path.GetFileName(_filePath)}: {ex.Message}");
                    Log.Error($"  line content: {(line.Length > 120 ? line[..120] + "..." : line)}");
                }
            }
        }

        if (errorCount > 5)
            Log.Error($"[JsonLineStore<{typeof(T).Name}>] ... and {errorCount - 5} more corrupt lines in {Path.GetFileName(_filePath)}");

        if (errorCount > 0)
            Log.Warn($"[JsonLineStore<{typeof(T).Name}>] loaded {results.Count} records, skipped {errorCount} corrupt lines from {Path.GetFileName(_filePath)}");

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
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var line = JsonSerializer.Serialize(record, s_options);
            File.AppendAllText(_filePath, line + Environment.NewLine);
            _cache = null; // invalidate cache
        }
        catch (Exception ex)
        {
            Log.Error($"[JsonLineStore<{typeof(T).Name}>] failed to append to {Path.GetFileName(_filePath)}: {ex.GetType().Name}: {ex.Message}");
            throw; // re-throw so tool can catch and return error message
        }
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
        _cache = null; // invalidate cache
    }

    /// <summary>
    /// Returns true if the JSONL file exists and has at least one record.
    /// Uses the in-memory cache when available to avoid disk I/O.
    /// </summary>
    public bool HasData()
    {
        if (_cache is not null)
            return _cache.Count > 0;

        return File.Exists(_filePath) && new FileInfo(_filePath).Length > 0;
    }

    /// <summary>
    /// Returns the number of records. Uses the in-memory cache when available.
    /// </summary>
    public int Count()
    {
        if (_cache is not null)
            return _cache.Count;

        if (!File.Exists(_filePath))
            return 0;

        return File.ReadLines(_filePath).Count(l => !string.IsNullOrWhiteSpace(l));
    }
}
