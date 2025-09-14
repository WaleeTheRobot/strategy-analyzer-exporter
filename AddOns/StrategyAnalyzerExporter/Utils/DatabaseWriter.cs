using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Database
{
    public sealed class DatabaseWriter : IDisposable
    {
        private readonly string _tableName;
        private SQLiteConnection _connection;
        private bool _disposed;

        public DatabaseWriter(string databasePath, string tableName = "FeaturesDataBars")
        {
            _tableName = tableName;

            // Ensure directory exists
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Create database file if it doesn't exist
            if (!File.Exists(databasePath))
                SQLiteConnection.CreateFile(databasePath);

            // Connect and optimize
            _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
            _connection.Open();
            ApplyOptimizations();
        }

        private void ApplyOptimizations()
        {
            ExecuteCommand("PRAGMA journal_mode=WAL");
            ExecuteCommand("PRAGMA synchronous=NORMAL");
            ExecuteCommand("PRAGMA cache_size=-200000");
            ExecuteCommand("PRAGMA temp_store=MEMORY");
            ExecuteCommand("PRAGMA busy_timeout=5000");
        }

        public void EnsureTableExists<T>()
        {
            if (TableExists()) return;
            CreateTableFromType<T>();
        }

        private void CreateTableFromType<T>()
        {
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .ToArray();

            var columnDefs = properties.Select(p => $"{p.Name} {GetSQLiteType(p.PropertyType)}");

            var sql = $@"CREATE TABLE {_tableName} (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                {string.Join(",\n                ", columnDefs)}
            )";

            ExecuteCommand(sql);
        }

        public void InsertBatch<T>(List<T> items)
        {
            if (items.Count == 0 || _disposed) return;

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToArray();

            var columnNames = properties.Select(p => p.Name);
            var paramNames = properties.Select(p => $"@{p.Name}");

            var sql = $"INSERT INTO {_tableName} ({string.Join(", ", columnNames)}) " +
                      $"VALUES ({string.Join(", ", paramNames)})";

            using var transaction = _connection.BeginTransaction(IsolationLevel.Serializable);
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;

            foreach (var prop in properties)
                command.Parameters.Add($"@{prop.Name}", GetDbType(prop.PropertyType));

            try
            {
                foreach (var item in items)
                {
                    for (int i = 0; i < properties.Length; i++)
                        command.Parameters[i].Value = properties[i].GetValue(item) ?? DBNull.Value;

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void CheckpointAndFinalize()
        {
            if (_disposed || _connection == null) return;

            try
            {
                // Merge WAL into the main DB; TRUNCATE removes -wal pages
                ExecuteCommand("PRAGMA wal_checkpoint(TRUNCATE);");

                // Switch to DELETE mode so -wal / -shm files are removed and future writes go direct
                ExecuteCommand("PRAGMA journal_mode=DELETE;");
            }
            catch
            {
                // Swallow
            }
        }

        private bool TableExists()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", _tableName);
            return cmd.ExecuteScalar() != null;
        }

        private static string GetSQLiteType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;

            if (t == typeof(int) || t == typeof(long) || t == typeof(bool)) return "INTEGER";
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return "REAL";
            if (t == typeof(DateTime)) return "TEXT";
            if (t == typeof(byte[])) return "BLOB";
            return "TEXT";
        }

        private static DbType GetDbType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;

            if (t == typeof(int) || t == typeof(long) || t == typeof(bool)) return DbType.Int64;
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return DbType.Double;
            if (t == typeof(DateTime)) return DbType.String;
            if (t == typeof(byte[])) return DbType.Binary;
            return DbType.String;
        }

        private void ExecuteCommand(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Ensure WAL is merged and aux files removed before closing
                CheckpointAndFinalize();
            }
            finally
            {
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
            }
        }
    }
}
