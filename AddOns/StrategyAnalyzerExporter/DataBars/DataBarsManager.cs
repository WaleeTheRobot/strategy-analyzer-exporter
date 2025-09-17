using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Database;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils;
using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class DataBarsManager : IDisposable
    {
        public enum TimeFrame { Primary, Secondary, Tertiary }

        private readonly DataBarsConfig _config;
        private readonly Dictionary<TimeFrame, List<DataBar>> _dataBars;
        private readonly Queue<FeaturesDataBar> _pendingWrites;

        private DatabaseWriter _dbWriter;
        private readonly int _flushSize;
        private readonly TimeSpan _flushInterval;
        private DateTime _lastFlushTime;
        private bool _disposed;
        private bool _tableEnsured;

        public DataBarsManager(DataBarsConfig config)
        {
            _config = config ?? new DataBarsConfig();
            _flushSize = _config.FlushSize;
            _flushInterval = TimeSpan.FromSeconds(_config.FlushIntervalSeconds);

            _dataBars = new Dictionary<TimeFrame, List<DataBar>>
            {
                { TimeFrame.Primary,   new List<DataBar>() },
                { TimeFrame.Secondary, new List<DataBar>() },
                { TimeFrame.Tertiary,  new List<DataBar>() }
            };

            _pendingWrites = new Queue<FeaturesDataBar>();
            _lastFlushTime = DateTime.UtcNow;

            InitializeDatabase();
        }

        public void OnNewPrimaryBarAvailable(bool canCalculateFeatures, DataBar dataBar)
        {
            _dataBars[TimeFrame.Primary].Add(dataBar);
            if (!canCalculateFeatures || _disposed) return;

            var featuresDataBar = CreateFeaturesDataBar(dataBar);

            if (_config.EnablePrintDataBar)
                BarPrinter.Print(featuresDataBar, "Feature DataBar");

            if (_config.EnableWriteToDatabase)
                QueueForDatabase(featuresDataBar);
        }

        public void OnNewSecondaryBarAvailable(DataBar prev) => _dataBars[TimeFrame.Secondary].Add(prev);

        public void OnNewTertiaryBarAvailable(DataBar prev) => _dataBars[TimeFrame.Tertiary].Add(prev);

        private FeaturesDataBar CreateFeaturesDataBar(DataBar baseBar)
        {
            var featuresDataBar = new FeaturesDataBar
            {
                Time = baseBar.Time,
                Day = baseBar.Day,
                Open = baseBar.Open,
                High = baseBar.High,
                Low = baseBar.Low,
                Close = baseBar.Close,
                Volume = baseBar.Volume,

                Primary = TimeFrameFeatures.ExtractFrom(_dataBars[TimeFrame.Primary], _config),
                Secondary = TimeFrameFeatures.ExtractFrom(_dataBars[TimeFrame.Secondary], _config),
                Tertiary = TimeFrameFeatures.ExtractFrom(_dataBars[TimeFrame.Tertiary], _config)
            };

            return featuresDataBar;
        }

        #region Database Handling

        public void FlushPending() => FlushToDatabase();
        public void FinalizeAndClose() => FinalizeInternal();
        public void Dispose() => FinalizeInternal();

        private void InitializeDatabase()
        {
            if (!_config.EnableWriteToDatabase || string.IsNullOrEmpty(_config.DatabasePath))
                return;

            var tableName = string.IsNullOrWhiteSpace(_config.TableName) ? "FeaturesDataBars" : _config.TableName;

            _dbWriter = new DatabaseWriter(
                _config.DatabasePath,
                tableName,
                _config.UseFloat32
            );

            _dbWriter.EnsureTableExists<FeaturesDataBar>();
            _tableEnsured = true;
        }

        private void QueueForDatabase(FeaturesDataBar bar)
        {
            _pendingWrites.Enqueue(bar);

            bool sizeDue = _pendingWrites.Count >= _flushSize;
            bool timeDue = (DateTime.UtcNow - _lastFlushTime) >= _flushInterval;

            if (sizeDue || timeDue)
                FlushToDatabase();
        }

        private void FlushToDatabase()
        {
            if (_disposed || _dbWriter == null || _pendingWrites.Count == 0) return;

            var batch = new List<FeaturesDataBar>(_pendingWrites.Count);
            while (_pendingWrites.Count > 0)
                batch.Add(_pendingWrites.Dequeue());

            try
            {
                if (!_tableEnsured)
                {
                    _dbWriter.EnsureTableExists<FeaturesDataBar>();
                    _tableEnsured = true;
                }

                _dbWriter.InsertBatch(batch);
                _lastFlushTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                EventManager.PrintMessage($"Database flush error: {ex.Message}", true);
            }
        }

        private void FinalizeInternal()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Flush any queued rows
                FlushToDatabase();

                // Fold WAL into the main file (optional but nice)
                try { _dbWriter?.Checkpoint(); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                EventManager.PrintMessage($"Cleanup error: {ex.Message}", true);
            }
            finally
            {
                // Deterministic release of the file handle
                try { _dbWriter?.Dispose(); } catch { /* ignore */ }
                _dbWriter = null;
            }
        }

        #endregion
    }
}
