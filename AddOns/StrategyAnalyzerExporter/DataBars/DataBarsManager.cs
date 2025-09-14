using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Database;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.MovingAverages;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.Price;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils;
using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class DataBarsManager : IDisposable
    {
        public enum TimeFrame { Primary, Secondary, Tertiary }

        public class BarFeatures
        {
            public double MovingAverageDistance { get; set; }
            public double MovingAverageSlope { get; set; }
            public double OpenLocationValue { get; set; }
            public double CloseLocationValue { get; set; }
        }

        private const int LOOKBACK_PERIOD = 9;
        private const int FLUSH_SIZE = 2000;
        private static readonly TimeSpan FLUSH_INTERVAL = TimeSpan.FromSeconds(5);

        private readonly DataBarsConfig _config;
        private readonly Dictionary<TimeFrame, List<DataBar>> _dataBars;
        private readonly Queue<FeaturesDataBar> _pendingWrites;

        private DatabaseWriter _dbWriter;
        private DateTime _lastFlushTime;
        private bool _disposed;

        public DataBarsManager(DataBarsConfig config)
        {
            _config = config;
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

        private void InitializeDatabase()
        {
            if (!_config.EnableWriteToDatabase || string.IsNullOrEmpty(_config.DatabasePath))
                return;

            _dbWriter = new DatabaseWriter(_config.DatabasePath);
            _dbWriter.EnsureTableExists<FeaturesDataBar>();
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
            var features = ExtractAllFeatures();
            return new FeaturesDataBar
            {
                Time = baseBar.Time,
                Day = baseBar.Day,
                Open = baseBar.Open,
                High = baseBar.High,
                Low = baseBar.Low,
                Close = baseBar.Close,
                Volume = baseBar.Volume,
                F_PrimaryMovingAverageDistance = features[TimeFrame.Primary].MovingAverageDistance,
                F_PrimaryMovingAverageSlope = features[TimeFrame.Primary].MovingAverageSlope,
                F_PrimaryOpenLocationValue = features[TimeFrame.Primary].OpenLocationValue,
                F_PrimaryCloseLocationValue = features[TimeFrame.Primary].CloseLocationValue,
                F_SecondaryMovingAverageDistance = features[TimeFrame.Secondary].MovingAverageDistance,
                F_SecondaryMovingAverageSlope = features[TimeFrame.Secondary].MovingAverageSlope,
                F_SecondaryOpenLocationValue = features[TimeFrame.Secondary].OpenLocationValue,
                F_SecondaryCloseLocationValue = features[TimeFrame.Secondary].CloseLocationValue,
                F_TertiaryMovingAverageDistance = features[TimeFrame.Tertiary].MovingAverageDistance,
                F_TertiaryMovingAverageSlope = features[TimeFrame.Tertiary].MovingAverageSlope,
                F_TertiaryOpenLocationValue = features[TimeFrame.Tertiary].OpenLocationValue,
                F_TertiaryCloseLocationValue = features[TimeFrame.Tertiary].CloseLocationValue,
            };
        }

        private Dictionary<TimeFrame, BarFeatures> ExtractAllFeatures()
        {
            var features = new Dictionary<TimeFrame, BarFeatures>();
            foreach (TimeFrame tf in Enum.GetValues(typeof(TimeFrame)))
                features[tf] = ExtractFeaturesForTimeFrame(tf);
            return features;
        }

        private BarFeatures ExtractFeaturesForTimeFrame(TimeFrame timeFrame)
        {
            var dataBars = _dataBars[timeFrame];
            var (dist, slope) = GetMovingAverageFeatures(dataBars);
            var (olv, clv) = GetPriceFeatures(dataBars);

            return new BarFeatures
            {
                MovingAverageDistance = dist,
                MovingAverageSlope = slope,
                OpenLocationValue = olv,
                CloseLocationValue = clv
            };
        }

        private static (double MovingAverageDistance, double MovingAverageSlope) GetMovingAverageFeatures(List<DataBar> dataBars)
        {
            if (dataBars == null || dataBars.Count == 0) return (0.0, 0.0);
            var lastBar = dataBars[dataBars.Count - 1];
            var distance = MovingAverages.GetCloseMovingAverageDistance(lastBar);
            var maSeries = SeriesExtractor.ExtractSeries(dataBars, SeriesExtractor.Field.MovingAverage);
            var slope = Slope.Calculate(maSeries, lookback: LOOKBACK_PERIOD);
            return (distance, slope);
        }

        private static (double OpenLocationValue, double CloseLocationValue) GetPriceFeatures(List<DataBar> dataBars)
        {
            if (dataBars == null || dataBars.Count == 0) return (0.0, 0.0);
            var lastBar = dataBars[dataBars.Count - 1];
            var olv = Price.GetOpenLocationValue(lastBar);
            var clv = Price.GetCloseLocationValue(lastBar);
            return (olv, clv);
        }

        #region Database Handling

        public void FlushPending() => FlushToDatabase();

        private void QueueForDatabase(FeaturesDataBar bar)
        {
            _pendingWrites.Enqueue(bar);

            bool sizeDue = _pendingWrites.Count >= FLUSH_SIZE;
            bool timeDue = (DateTime.UtcNow - _lastFlushTime) >= FLUSH_INTERVAL;

            if (sizeDue || timeDue)
                FlushToDatabase();
        }

        private void FlushToDatabase()
        {
            if (_dbWriter == null || _pendingWrites.Count == 0 || _disposed) return;

            var batch = new List<FeaturesDataBar>();
            while (_pendingWrites.Count > 0) batch.Add(_pendingWrites.Dequeue());

            try
            {
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
                FlushToDatabase();
                _dbWriter?.CheckpointAndFinalize(); // Merge WAL and remove -wal/-shm
            }
            catch (Exception ex)
            {
                EventManager.PrintMessage($"Cleanup error: {ex.Message}", true);
            }
            finally
            {
                _dbWriter?.Dispose();
                _dbWriter = null;
            }
        }

        public void FinalizeAndClose() => FinalizeInternal();

        public void Dispose() => FinalizeInternal();

        #endregion
    }
}
