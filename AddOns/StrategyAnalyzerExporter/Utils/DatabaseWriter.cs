using DuckDB.NET.Data;
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
        private DuckDBConnection _connection;
        private bool _disposed;

        public DatabaseWriter(string databasePath, string tableName = "FeatureDataBars", bool useFloat32 = true)
        {
            _tableName = tableName;
            _useFloat32 = useFloat32;

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            NativeDuckDb.EnsureLoaded(); // Load native duckdb.dll

            var cs = $"Data Source={databasePath}";
            _connection = new DuckDBConnection(cs);
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

            using var tx = _connection.BeginTransaction();

            try
            {
                foreach (var item in items)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;

                    // Extract values using flattening logic
                    var values = ExtractFlattenedValues(item, flatColumns);

                    for (int i = 0; i < values.Count; i++)
                    {
                        var prm = cmd.CreateParameter();
                        prm.Value = ConvertForStorage(values[i], flatColumns[i].PropertyType, _useFloat32);
                        cmd.Parameters.Add(prm);
                    }

                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
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

        private List<object> ExtractFlattenedValues(object item, List<FlatColumn> columns)
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
                _ => propertyName // fallback
            };
        }

        public void Checkpoint()
        {
            if (_disposed || _connection == null) return;
            try { ExecuteNonQuery("CHECKPOINT;"); } catch { /* ignore */ }
        }

        // Hard close to release file handles immediately
        public void CloseImmediately()
        {
            try { _connection?.Close(); } catch { /* ignore */ }
            try { _connection?.Dispose(); } catch { /* ignore */ }
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
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
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
    /// Do NOT add the native DLL as a NinjaScript "Reference".
    /// </summary>
    internal static class NativeDuckDb
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static void EnsureLoaded()
        {
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var asmDir = Path.GetDirectoryName(asmPath) ?? "";

            var candidates = new[]
            {
                asmDir,
                AppDomain.CurrentDomain.BaseDirectory ?? "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),  "NinjaTrader 8", "bin64")
            };

            string dllPath = null;
            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var cand = Path.Combine(dir, "duckdb.dll");
                if (File.Exists(cand)) { dllPath = cand; break; }
            }

            if (dllPath == null)
                throw new FileNotFoundException("duckdb.dll not found. Searched:\n" + string.Join("\n", candidates));

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
