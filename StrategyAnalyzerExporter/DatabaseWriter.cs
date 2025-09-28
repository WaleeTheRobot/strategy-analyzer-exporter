using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter;

/// <summary>
/// Highly optimized generic database writer that dynamically handles any type.
/// Uses aggressive caching and pre-compiled accessors to achieve near-constant time performance.
/// </summary>
public sealed class DatabaseWriter : IDisposable
{
    // Per-type cached metadata - compiled once, used forever
    private static readonly ConcurrentDictionary<Type, TypeMetadata> _typeCache = new ConcurrentDictionary<Type, TypeMetadata>();

    // Per-type method cache for AppendValue calls
    private static readonly ConcurrentDictionary<string, MethodInfo> _appendMethodCache = new ConcurrentDictionary<string, MethodInfo>();

    private readonly StrategyAnalyzerExporterConfig _config;
    private readonly string _tableName;

    private dynamic _connection;
    private bool _disposed;

    // Appender/transaction
    private object _appender;
    private dynamic _tx;
    private long _rowsSinceCommit;
    private int _commitsSinceCheckpoint;
    private DateTime _lastCommitUtc = DateTime.UtcNow;
    private DateTime _lastAppendUtc = DateTime.UtcNow;

    // Pre-resolved row methods
    private Type _rowType;
    private MethodInfo _miCreateRow;
    private MethodInfo _miAppendNull;
    private MethodInfo _miEndRow;
    private MethodInfo _miCloseAppender;

    // Assembly/type cache
    private static Assembly _duckDBAssembly;
    private static Type _connectionType;
    private static bool _typesLoaded;
    private static readonly object _loadLock = new object();

    public event Action<long> OnCommitted;

    /// <summary>
    /// Cached metadata for a specific type - compiled once for maximum performance
    /// </summary>
    private sealed class TypeMetadata
    {
        public readonly PropertyAccessor[] Properties;
        public readonly string CreateTableSql;
        public readonly string[] ColumnNames;

        public TypeMetadata(PropertyAccessor[] properties, string createTableSql, string[] columnNames)
        {
            Properties = properties;
            CreateTableSql = createTableSql;
            ColumnNames = columnNames;
        }
    }

    /// <summary>
    /// Pre-compiled property accessor with type information
    /// </summary>
    private sealed class PropertyAccessor
    {
        public readonly string Name;
        public readonly Type PropertyType;
        public readonly Type TargetType; // After nullable unwrapping
        public readonly Func<object, object> Getter;
        public readonly string DuckDbType;

        public PropertyAccessor(string name, Type propertyType, Type targetType, Func<object, object> getter, string duckDbType)
        {
            Name = name;
            PropertyType = propertyType;
            TargetType = targetType;
            Getter = getter;
            DuckDbType = duckDbType;
        }
    }

    static DatabaseWriter()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        if (args.Name.StartsWith("DuckDB.NET.Data") || args.Name.StartsWith("DuckDB.NET.Bindings"))
        {
            var file = args.Name.StartsWith("DuckDB.NET.Data") ? "DuckDB.NET.Data.dll" : "DuckDB.NET.Bindings.dll";
            var path = Path.Combine(@"C:\DuckDB", file);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    public DatabaseWriter(StrategyAnalyzerExporterConfig config, string tableName)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(_config.DatabasePath))
            throw new ArgumentException("Database path is required.", nameof(config.DatabasePath));

        _tableName = string.IsNullOrWhiteSpace(tableName) ? "Features" : tableName;

        var directory = Path.GetDirectoryName(_config.DatabasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        LoadDuckDBTypes();
        NativeDuckDb.EnsureLoaded();

        var cs = "Data Source=" + _config.DatabasePath;
        _connection = Activator.CreateInstance(_connectionType, cs);
        _connection.Open();
    }

    /// <summary>
    /// Generic table creation for any type - compiles metadata once and caches it
    /// </summary>
    public void EnsureTableExists<T>()
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));
        var sql = metadata.CreateTableSql.Replace("__TABLE_NAME__", Quote(_tableName));
        ExecuteNonQuery(sql);
    }

    /// <summary>
    /// Highly optimized generic batch insert using cached type metadata
    /// </summary>
    public void InsertBatch<T>(List<T> items)
    {
        if (_disposed || items?.Count == 0) return;

        var metadata = GetOrCreateTypeMetadata(typeof(T));
        EnsureAppenderResolved();

        foreach (var item in items)
        {
            var row = _miCreateRow.Invoke(_appender, null);

            // Ultra-fast property access using pre-compiled getters
            for (int i = 0; i < metadata.Properties.Length; i++)
            {
                var prop = metadata.Properties[i];
                var rawValue = prop.Getter(item);

                if (rawValue == null)
                {
                    _miAppendNull.Invoke(row, null);
                    continue;
                }

                // Handle type conversions and call appropriate AppendValue
                AppendOptimizedValue(row, prop, rawValue);
            }

            _miEndRow.Invoke(row, null);

            _rowsSinceCommit++;
            _lastAppendUtc = DateTime.UtcNow;

            // Commit logic
            if (_config.CommitEveryRows > 0 && _rowsSinceCommit >= _config.CommitEveryRows)
            {
                CommitAndMaybeCheckpoint();
                continue;
            }

            MaybeCommitByTime();
        }
    }

    /// <summary>
    /// Optimized value appending with cached method resolution
    /// </summary>
    private void AppendOptimizedValue(object row, PropertyAccessor prop, object rawValue)
    {
        object valueToStore = rawValue;
        Type argType = prop.TargetType;

        // Handle special cases
        if (prop.TargetType.IsEnum)
        {
            valueToStore = rawValue.ToString();
            argType = typeof(string);
        }
        else if (_config.UseFloat32 && prop.TargetType == typeof(double))
        {
            valueToStore = (float)(double)rawValue;
            argType = typeof(float);
        }
        else if (argType == typeof(ulong))
        {
            valueToStore = unchecked((long)(ulong)rawValue);
            argType = typeof(long);
        }

        // Get cached AppendValue method
        var method = GetCachedAppendMethod(_rowType, argType);
        method.Invoke(row, new[] { valueToStore });
    }

    /// <summary>
    /// Get or create cached type metadata - this is where the magic happens
    /// </summary>
    private TypeMetadata GetOrCreateTypeMetadata(Type type)
    {
        return _typeCache.GetOrAdd(type, t =>
        {
            // Get properties in stable order - more permissive filtering
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0) // Removed CanWrite requirement
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .ToArray();

            // Debug: Print properties found
            if (props.Length == 0)
            {
                // If no properties found, get ALL properties for debugging
                var allProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var debugInfo = string.Join(", ", allProps.Select(p => $"{p.Name}({p.CanRead}/{p.CanWrite})"));
                throw new InvalidOperationException($"No suitable properties found for type {t.Name}. All properties: {debugInfo}");
            }

            var accessors = new PropertyAccessor[props.Length];
            var columnDefs = new List<string>();
            var columnNames = new string[props.Length];

            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var duckType = GetDuckDbType(targetType, _config.UseFloat32);
                var getter = CompileGetter(prop);

                accessors[i] = new PropertyAccessor(prop.Name, prop.PropertyType, targetType, getter, duckType);
                columnNames[i] = prop.Name;
                columnDefs.Add($"{Quote(prop.Name)} {duckType}");
            }

            var createTableSql = $"CREATE TABLE IF NOT EXISTS __TABLE_NAME__ ({string.Join(", ", columnDefs)});";

            return new TypeMetadata(accessors, createTableSql, columnNames);
        });
    }

    /// <summary>
    /// Compile ultra-fast property getter using expression trees
    /// </summary>
    private static Func<object, object> CompileGetter(PropertyInfo prop)
    {
        var instance = Expression.Parameter(typeof(object), "x");
        var cast = Expression.Convert(instance, prop.DeclaringType);
        var access = Expression.Property(cast, prop);
        var box = Expression.Convert(access, typeof(object));
        return Expression.Lambda<Func<object, object>>(box, instance).Compile();
    }

    /// <summary>
    /// Get cached AppendValue method for specific row type and argument type
    /// </summary>
    private MethodInfo GetCachedAppendMethod(Type rowType, Type argType)
    {
        var key = $"{rowType.FullName}|{argType.FullName}";
        return _appendMethodCache.GetOrAdd(key, _ =>
        {
            var method = rowType.GetMethod("AppendValue", new[] { argType });

            // Fallback logic for edge cases
            if (method == null && argType == typeof(double))
            {
                method = rowType.GetMethod("AppendValue", new[] { typeof(decimal) });
            }
            if (method == null && argType.IsEnum)
            {
                method = rowType.GetMethod("AppendValue", new[] { typeof(int) });
            }
            if (method == null)
                throw new NotSupportedException($"No AppendValue overload for {argType.FullName}");

            return method;
        });
    }

    /// <summary>
    /// Efficient DuckDB type mapping
    /// </summary>
    private static string GetDuckDbType(Type type, bool useFloat32)
    {
        if (type.IsEnum) return "VARCHAR";
        if (type == typeof(string)) return "VARCHAR";
        if (type == typeof(byte[])) return "BLOB";
        if (type == typeof(bool)) return "BOOLEAN";
        if (type == typeof(byte) || type == typeof(sbyte)) return "TINYINT";
        if (type == typeof(short) || type == typeof(ushort)) return "SMALLINT";
        if (type == typeof(int) || type == typeof(uint)) return "INTEGER";
        if (type == typeof(long) || type == typeof(ulong)) return "BIGINT";
        if (type == typeof(float)) return "REAL";
        if (type == typeof(double)) return useFloat32 ? "REAL" : "DOUBLE";
        if (type == typeof(decimal)) return "DECIMAL(28,9)";
        if (type == typeof(DateTime)) return "TIMESTAMP";
        if (type == typeof(TimeSpan)) return "INTERVAL";
        return "VARCHAR";
    }

    private static void LoadDuckDBTypes()
    {
        if (_typesLoaded) return;
        lock (_loadLock)
        {
            if (_typesLoaded) return;

            var dataAssemblyPath = @"C:\DuckDB\DuckDB.NET.Data.dll";
            if (!File.Exists(dataAssemblyPath))
                throw new FileNotFoundException("DuckDB.NET.Data.dll not found at " + dataAssemblyPath);

            _duckDBAssembly = Assembly.LoadFrom(dataAssemblyPath);
            _connectionType = _duckDBAssembly.GetType("DuckDB.NET.Data.DuckDBConnection")
                              ?? throw new TypeLoadException("Could not find DuckDBConnection type");
            _typesLoaded = true;
        }
    }

    private void EnsureAppenderResolved()
    {
        if (_appender != null && _miCreateRow != null) return;

        _tx = _connection.BeginTransaction();
        _appender = _connection.CreateAppender("main", _tableName);

        var t = _appender.GetType();
        _miCreateRow = t.GetMethod("CreateRow", BindingFlags.Public | BindingFlags.Instance);
        _miCloseAppender = t.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);

        var row = _miCreateRow.Invoke(_appender, null);
        _rowType = row.GetType();
        _miAppendNull = _rowType.GetMethod("AppendNull", BindingFlags.Public | BindingFlags.Instance);
        _miEndRow = _rowType.GetMethod("EndRow", BindingFlags.Public | BindingFlags.Instance);

        _rowsSinceCommit = 0;
        _lastCommitUtc = DateTime.UtcNow;
    }

    public void ForceCommitAndCheckpoint(bool checkpoint = true)
    {
        long committedRows = 0;
        try
        {
            if (_appender != null && _miCloseAppender != null)
            {
                try { _miCloseAppender.Invoke(_appender, null); } catch { }
            }

            if (_tx != null)
            {
                _tx.Commit();
                if (_tx is IDisposable disp) disp.Dispose();
                committedRows = _rowsSinceCommit;
            }
        }
        finally
        {
            _appender = null;
            _tx = null;
            _rowType = null;
            _miCreateRow = _miAppendNull = _miEndRow = _miCloseAppender = null;

            _lastCommitUtc = DateTime.UtcNow;

            if (committedRows > 0)
                OnCommitted?.Invoke(committedRows);

            _rowsSinceCommit = 0;

            if (checkpoint)
            {
                try { ExecuteNonQuery("CHECKPOINT;"); } catch { }
                _commitsSinceCheckpoint = 0;
            }
        }
    }

    private void MaybeCommitByTime()
    {
        var now = DateTime.UtcNow;

        if (_config.MaxTxDurationSeconds > 0 &&
            (now - _lastCommitUtc) >= TimeSpan.FromSeconds(_config.MaxTxDurationSeconds) &&
            _rowsSinceCommit > 0)
        {
            CommitAndMaybeCheckpoint();
            return;
        }

        if (_config.IdleTailCommitSeconds > 0 &&
            (now - _lastAppendUtc) >= TimeSpan.FromSeconds(_config.IdleTailCommitSeconds) &&
            _rowsSinceCommit > 0)
        {
            CommitAndMaybeCheckpoint();
        }
    }

    private void CommitAndMaybeCheckpoint()
    {
        long committedRows = 0;

        try
        {
            if (_appender != null && _miCloseAppender != null)
            {
                try { _miCloseAppender.Invoke(_appender, null); } catch { }
            }

            if (_tx != null)
            {
                _tx.Commit();
                if (_tx is IDisposable disp) disp.Dispose();
                committedRows = _rowsSinceCommit;
            }
        }
        finally
        {
            _appender = null;
            _tx = null;
            _rowType = null;
            _miCreateRow = _miAppendNull = _miEndRow = _miCloseAppender = null;

            _lastCommitUtc = DateTime.UtcNow;

            if (committedRows > 0)
                OnCommitted?.Invoke(committedRows);

            _rowsSinceCommit = 0;

            if (_config.CheckpointEveryCommits > 0)
            {
                _commitsSinceCheckpoint++;
                if (_commitsSinceCheckpoint >= _config.CheckpointEveryCommits)
                {
                    try { ExecuteNonQuery("CHECKPOINT;"); } catch { }
                    _commitsSinceCheckpoint = 0;
                }
            }

            // Re-open immediately for continued ingest
            EnsureAppenderResolved();
        }
    }

    private static string Quote(string id) => string.IsNullOrEmpty(id) ? id : "\"" + id.Replace("\"", "\"\"") + "\"";

    private void ExecuteNonQuery(string sql)
    {
        dynamic cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        finally
        {
            try { if (cmd is IDisposable d) d.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ForceCommitAndCheckpoint(checkpoint: true); } catch { }
        try { CloseImmediately(); } catch { }
    }

    public void CloseImmediately()
    {
        if (_connection == null) return;
        try { _connection.Close(); } catch { }
        try { if (_connection is IDisposable d) d.Dispose(); } catch { }
        _connection = null;
    }
}

/// <summary>
/// Helper class to ensure native DuckDB library is loaded properly
/// </summary>
internal static class NativeDuckDb
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    public static void EnsureLoaded()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NinjaTrader 8", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NinjaTrader 8", "bin64"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom"),
            @"C:\DuckDB",
            AppDomain.CurrentDomain.BaseDirectory ?? "",
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ""
        };

        string dllPath = null;
        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var cand = Path.Combine(dir, "duckdb.dll");
            if (File.Exists(cand)) { dllPath = cand; break; }
        }

        if (dllPath == null)
            throw new FileNotFoundException("duckdb.dll not found in any of the expected locations.");

        var nativeDir = Path.GetDirectoryName(dllPath) ?? "";
        SetDllDirectory(nativeDir);

        var handle = LoadLibrary(dllPath);
        if (handle == IntPtr.Zero)
        {
            var err = new Win32Exception(Marshal.GetLastWin32Error());
            throw new Win32Exception(err.NativeErrorCode,
                "LoadLibrary failed for '" + dllPath + "'. Ensure 64-bit DLL is installed. System message: " + err.Message);
        }
    }
}