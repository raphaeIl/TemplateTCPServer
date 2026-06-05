using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NTRSimulator.Common.Table;

namespace TemplateTCPServer.Table
{
    public sealed class TableService(ILogger<TableService> _logger) : ITableService
    {
        private readonly ILogger<TableService> logger = _logger;

        /// <summary>
        /// Type → deserialized IMessage instance. Each entry is the top-level "table" container
        /// (e.g. <c>CharacterExcelTable</c>) whose repeated field holds the individual rows.
        /// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> because handlers run on
        /// per-connection threads and may trigger lazy loads concurrently.
        /// </summary>
        private readonly ConcurrentDictionary<Type, IMessage> caches = new();

        /// <summary>
        /// Cached <see cref="MessageParser"/> instances per type, so we only pay the reflection
        /// cost once per table type over the lifetime of the process.
        /// </summary>
        private readonly ConcurrentDictionary<Type, MessageParser> parserCache = new();

        /// <summary>
        /// Base directory for all resource files. Defaults to <c>{BaseDirectory}/../Resources</c>.
        /// Can be overridden via configuration (see <see cref="TableExtensions"/>).
        /// </summary>
        public static string ResourceDir = Path.Join(
            Path.GetDirectoryName(AppContext.BaseDirectory), "Resources");

        // ──────────────────────────────────────────────────────────────
        //  Load
        // ──────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public T GetTable<T>(bool bypassCache = false) where T : IMessage<T>, new()
        {
            var type = typeof(T);

            if (!bypassCache && caches.TryGetValue(type, out var cached))
                return (T)cached;

            var tableDir = Path.Combine(ResourceDir, "Tables");
            var bytesFilePath = Path.Combine(tableDir, $"{type.Name}.bytes");

            if (!File.Exists(bytesFilePath))
                throw new FileNotFoundException(
                    $"Table file not found: {bytesFilePath}", bytesFilePath);

            var table = LoadBin<T>(bytesFilePath);
            caches[type] = table;

            logger.LogDebug("{Table} loaded and cached", type.Name);
            return table;
        }

        /// <inheritdoc />
        public void Preload(params Type[] tableTypes)
        {
            // Invoke GetTable<T>() via reflection for each supplied type.
            var method = typeof(TableService)
                .GetMethod(nameof(GetTable), BindingFlags.Public | BindingFlags.Instance)!;

            foreach (var type in tableTypes)
            {
                try
                {
                    var generic = method.MakeGenericMethod(type);
                    generic.Invoke(this, [false]);
                    logger.LogInformation("Preloaded {Table}", type.Name);
                }
                catch (Exception ex)
                {
                    var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
                    logger.LogWarning(actual, "Failed to preload {Table}", type.Name);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Dump
        // ──────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public string DumpJson<T>() where T : IMessage<T>, new()
        {
            var table = GetTable<T>();
            var formatter = new JsonFormatter(JsonFormatter.Settings.Default);
            return formatter.Format(table);
        }

        /// <inheritdoc />
        public void DumpJsonToFile<T>(string? outputPath = null) where T : IMessage<T>, new()
        {
            var json = DumpJson<T>();
            outputPath ??= Path.Combine(ResourceDir, "Dump", $"{typeof(T).Name}.json");

            var dir = Path.GetDirectoryName(outputPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            File.WriteAllText(outputPath, json);
            logger.LogInformation("Dumped {Table} to {Path}", typeof(T).Name, outputPath);
        }

        // ──────────────────────────────────────────────────────────────
        //  Download
        // ──────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public void DownloadTable<T>(string url, bool autoLoad = true) where T : IMessage<T>, new()
        {
            var tableDir = Path.Combine(ResourceDir, "Tables");
            Directory.CreateDirectory(tableDir);

            var outputPath = Path.Combine(tableDir, $"{typeof(T).Name}.bytes");

            // Synchronous HTTP — matches the project's fully-synchronous execution model.
            using var client = new HttpClient();
            var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();

            File.WriteAllBytes(outputPath, bytes);
            logger.LogInformation("Downloaded {Table} ({Bytes} bytes) from {Url}",
                typeof(T).Name, bytes.Length, url);

            if (autoLoad)
                GetTable<T>(bypassCache: true);
        }

        // ──────────────────────────────────────────────────────────────
        //  Cache management
        // ──────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public void ClearCache()
        {
            var count = caches.Count;
            caches.Clear();
            logger.LogInformation("Table cache cleared ({Count} entries evicted)", count);
        }

        // ──────────────────────────────────────────────────────────────
        //  Internal — protobuf deserialization via reflection
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a <c>.bytes</c> file and deserializes it into <typeparamref name="T"/>
        /// using the generated static <c>MessageParser&lt;T&gt;</c>.
        /// </summary>
        private T LoadBin<T>(string filePath) where T : IMessage<T>, new()
        {
            var bytes = File.ReadAllBytes(filePath);

            // Hook: table encryption / XOR can be applied here.
            // TableEncryptionService.XOR(typeof(T).Name, bytes);

            var parser = GetParser<T>();
            return (T)parser.ParseFrom(bytes);
        }

        /// <summary>
        /// Resolves the static <c>Parser</c> property on the generated protobuf type via
        /// reflection and caches it for future use.
        /// </summary>
        private MessageParser GetParser<T>() where T : IMessage<T>, new()
        {
            return parserCache.GetOrAdd(typeof(T), static type =>
            {
                var parserProp = type.GetProperty("Parser",
                    BindingFlags.Static | BindingFlags.Public)
                    ?? throw new InvalidOperationException(
                        $"Type {type.FullName} does not have a static Parser property. " +
                        $"Is it a protobuf-generated IMessage type?");

                return (MessageParser)parserProp.GetValue(null)!;
            });
        }
    }

    public interface ITableService
    {
        /// <summary>
        /// Loads and returns the table of type <typeparamref name="T"/> from
        /// <c>Resources/Tables/{TypeName}.bytes</c>. Returns the cached instance on
        /// subsequent calls unless <paramref name="bypassCache"/> is <c>true</c>.
        /// </summary>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the corresponding <c>.bytes</c> file does not exist.
        /// </exception>
        T GetTable<T>(bool bypassCache = false) where T : IMessage<T>, new();

        /// <summary>
        /// Bulk-loads multiple table types. Failures are logged but do not throw.
        /// Pass the protobuf-generated table types:
        /// <c>tableService.Preload(typeof(CharacterExcelTable), typeof(ItemTable))</c>
        /// </summary>
        void Preload(params Type[] tableTypes);

        /// <summary>
        /// Returns the cached (or freshly loaded) table as a JSON string using
        /// <see cref="JsonFormatter"/>.
        /// </summary>
        string DumpJson<T>() where T : IMessage<T>, new();

        /// <summary>
        /// Dumps the table to a JSON file. Defaults to <c>Resources/Dump/{TypeName}.json</c>.
        /// </summary>
        void DumpJsonToFile<T>(string? outputPath = null) where T : IMessage<T>, new();

        /// <summary>
        /// Downloads a <c>.bytes</c> file from <paramref name="url"/> and saves it to
        /// <c>Resources/Tables/{TypeName}.bytes</c>. Optionally auto-loads into the cache.
        /// </summary>
        void DownloadTable<T>(string url, bool autoLoad = true) where T : IMessage<T>, new();

        /// <summary>
        /// Evicts all cached tables. Subsequent <see cref="GetTable{T}"/> calls will
        /// re-read from disk.
        /// </summary>
        void ClearCache();
    }
}
