using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Database
{
    /// <summary>
    /// DuckDB writer that dynamically creates a table from FeaturesDataBar,
    /// and inserts batches efficiently. If useFloat32 = true, double properties are stored as REAL (float32);
    /// otherwise they're stored as DOUBLE (float64).
    /// </summary>
    public sealed class DatabaseWriter : IDisposable
    {
        // Handle flattening of nested TimeFrameFeatures
        private sealed class FlatColumn
        {
            public string ColumnName { get; set; }
            public PropertyInfo Property { get; set; }
            public PropertyInfo NestedProperty { get; set; } // For TimeFrameFeatures properties
            public Type PropertyType { get; set; }
        }

        private readonly string _tableName;
        private readonly bool _useFloat32;
        private dynamic _connection;
        private bool _disposed;

        // Assembly and type caching
        private static Assembly _duckDBAssembly;
        private static Type _connectionType;
        private static bool _typesLoaded = false;
        private static readonly object _loadLock = new object();

        static DatabaseWriter()
        {
            // Set up assembly resolver
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (args.Name.StartsWith("DuckDB.NET.Data") || args.Name.StartsWith("DuckDB.NET.Bindings"))
            {
                var fileName = args.Name.StartsWith("DuckDB.NET.Data") ? "DuckDB.NET.Data.dll" : "DuckDB.NET.Bindings.dll";
                var path = Path.Combine(@"C:\DuckDB", fileName);

                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        }

        private static void LoadDuckDBTypes()
        {
            if (_typesLoaded) return;

            lock (_loadLock)
            {
                if (_typesLoaded) return;

                try
                {
                    var dataAssemblyPath = @"C:\DuckDB\DuckDB.NET.Data.dll";
                    if (!File.Exists(dataAssemblyPath))
                        throw new FileNotFoundException($"DuckDB.NET.Data.dll not found at {dataAssemblyPath}");

                    _duckDBAssembly = Assembly.LoadFrom(dataAssemblyPath);
                    _connectionType = _duckDBAssembly.GetType("DuckDB.NET.Data.DuckDBConnection");

                    if (_connectionType == null)
                        throw new TypeLoadException("Could not find DuckDBConnection type");

                    _typesLoaded = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to load DuckDB types: {ex.Message}", ex);
                }
            }
        }

        public DatabaseWriter(string databasePath, string tableName = "FeatureDataBars", bool useFloat32 = true)
        {
            _tableName = tableName;
            _useFloat32 = useFloat32;

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Load DuckDB types and native library
            LoadDuckDBTypes();
            NativeDuckDb.EnsureLoaded();

            // Create connection using Activator.CreateInstance
            var cs = $"Data Source={databasePath}";
            _connection = Activator.CreateInstance(_connectionType, cs);

            // Use dynamic to call Open - no ambiguity
            _connection.Open();

            ApplyOptimizations();
        }

        private void ApplyOptimizations()
        {
            try { ExecuteNonQuery("PRAGMA threads=4;"); } catch { /* ignore */ }
        }

        public void EnsureTableExists<T>() => CreateTableIfNotExistsFromType<T>();

        private void CreateTableIfNotExistsFromType<T>()
        {
            var flatColumns = GetFlattenedColumns(typeof(T));
            var colDefs = flatColumns.Select(col => $"{Quote(col.ColumnName)} {GetDuckDbType(col.PropertyType, _useFloat32)}");

            var sql = $@"
                CREATE TABLE IF NOT EXISTS {Quote(_tableName)} (
                    {string.Join(",\n    ", colDefs)}
                );";
            ExecuteNonQuery(sql);
        }

        public void InsertBatch<T>(List<T> items)
        {
            if (_disposed || items == null || items.Count == 0) return;

            var flatColumns = GetFlattenedColumns(typeof(T));
            var colNames = flatColumns.Select(col => Quote(col.ColumnName)).ToArray();
            var placeholders = string.Join(", ", Enumerable.Repeat("?", flatColumns.Count));

            var sql = $"INSERT INTO {Quote(_tableName)} ({string.Join(", ", colNames)}) VALUES ({placeholders})";

            try
            {
                // Use dynamic calls to avoid ambiguity
                using (dynamic tx = _connection.BeginTransaction())
                {
                    foreach (var item in items)
                    {
                        using (dynamic cmd = _connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = sql;

                            // Extract values using flattening logic
                            var values = ExtractFlattenedValues(item, flatColumns);

                            // Add parameters using dynamic
                            for (int i = 0; i < values.Count; i++)
                            {
                                dynamic param = cmd.CreateParameter();
                                param.Value = ConvertForStorage(values[i], flatColumns[i].PropertyType, _useFloat32);
                                cmd.Parameters.Add(param);
                            }

                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Database insert failed: {ex.Message}", ex);
            }
        }

        private List<FlatColumn> GetFlattenedColumns(Type type)
        {
            var columns = new List<FlatColumn>();
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                           .Where(p => p.CanRead && p.CanWrite)
                           .OrderBy(p => p.Name, StringComparer.Ordinal)
                           .ToArray();

            foreach (var prop in props)
            {
                // Check if this is a TimeFrameFeatures property
                if (IsTimeFrameFeaturesProperty(prop))
                {
                    var prefix = GetTimeFramePrefix(prop.Name);
                    var timeFrameProps = prop.PropertyType
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.PropertyType == typeof(double))
                        .OrderBy(p => p.Name, StringComparer.Ordinal);

                    foreach (var nestedProp in timeFrameProps)
                    {
                        columns.Add(new FlatColumn
                        {
                            ColumnName = $"F_{prefix}{nestedProp.Name}",
                            Property = prop,
                            NestedProperty = nestedProp,
                            PropertyType = nestedProp.PropertyType
                        });
                    }
                }
                else
                {
                    // Regular property - keep as-is
                    columns.Add(new FlatColumn
                    {
                        ColumnName = prop.Name,
                        Property = prop,
                        PropertyType = prop.PropertyType
                    });
                }
            }

            return columns;
        }

        private static List<object> ExtractFlattenedValues(object item, List<FlatColumn> columns)
        {
            var values = new List<object>();

            foreach (var col in columns)
            {
                if (col.NestedProperty != null)
                {
                    // Extract from nested TimeFrameFeatures
                    var timeFrameObj = col.Property.GetValue(item);
                    var value = timeFrameObj != null ? col.NestedProperty.GetValue(timeFrameObj) : null;
                    values.Add(value);
                }
                else
                {
                    // Regular property
                    values.Add(col.Property.GetValue(item));
                }
            }

            return values;
        }

        private static bool IsTimeFrameFeaturesProperty(PropertyInfo prop)
        {
            return prop.PropertyType.Name == "TimeFrameFeatures";
        }

        private static string GetTimeFramePrefix(string propertyName)
        {
            return propertyName switch
            {
                "Primary" => "Primary",
                "Secondary" => "Secondary",
                "Tertiary" => "Tertiary",
                _ => propertyName
            };
        }

        public void Checkpoint()
        {
            if (_disposed || _connection == null) return;
            try { ExecuteNonQuery("CHECKPOINT;"); } catch { /* ignore */ }
        }

        public void CloseImmediately()
        {
            if (_connection == null) return;

            try
            {
                _connection.Close();
            }
            catch
            {
                /* ignore */
            }

            try
            {
                ((IDisposable)_connection)?.Dispose();
            }
            catch
            {
                /* ignore */
            }

            _connection = null;
        }

        private static string GetDuckDbType(Type type, bool useFloat32)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;

            if (t == typeof(bool)) return "BOOLEAN";
            if (t == typeof(byte)) return "TINYINT";
            if (t == typeof(short)) return "SMALLINT";
            if (t == typeof(int)) return "INTEGER";
            if (t == typeof(long)) return "BIGINT";

            if (t == typeof(float)) return "REAL";
            if (t == typeof(double)) return useFloat32 ? "REAL" : "DOUBLE";
            if (t == typeof(decimal)) return "DECIMAL(28,9)";

            if (t == typeof(DateTime)) return "TIMESTAMP";
            if (t == typeof(TimeSpan)) return "INTERVAL";

            if (t == typeof(byte[])) return "BLOB";

            return "VARCHAR";
        }

        private static object ConvertForStorage(object value, Type type, bool useFloat32)
        {
            if (value == null) return DBNull.Value;

            var t = Nullable.GetUnderlyingType(type) ?? type;

            // Downcast doubles to float32 if requested
            if (useFloat32 && t == typeof(double))
                return (float)(double)value;

            return value;
        }

        private static string Quote(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        private void ExecuteNonQuery(string sql)
        {
            using (dynamic cmd = _connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Checkpoint(); } catch { /* ignore */ }
            CloseImmediately();
        }
    }

    /// <summary>
    /// Robust native loader that finds and loads duckdb.dll before opening the connection.
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
                AppDomain.CurrentDomain.BaseDirectory ?? "",
                Assembly.GetExecutingAssembly().Location != null ? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) : ""
            };

            string dllPath = null;
            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var cand = Path.Combine(dir, "duckdb.dll");
                if (File.Exists(cand))
                {
                    dllPath = cand;
                    break;
                }
            }

            if (dllPath == null)
            {
                var searchedPaths = string.Join("\n", candidates.Where(c => !string.IsNullOrWhiteSpace(c)));
                throw new FileNotFoundException($"duckdb.dll not found. Searched:\n{searchedPaths}");
            }

            var nativeDir = Path.GetDirectoryName(dllPath) ?? "";
            SetDllDirectory(nativeDir);

            var handle = LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                var err = new Win32Exception(Marshal.GetLastWin32Error());
                throw new Win32Exception(err.NativeErrorCode,
                    $"LoadLibrary failed for '{dllPath}'. Ensure 64-bit DLL installed. System message: {err.Message}");
            }
        }
    }
}
