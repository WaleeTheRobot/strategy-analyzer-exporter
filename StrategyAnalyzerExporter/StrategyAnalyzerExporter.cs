using FeatureEngineering;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using NinjaTrader.NinjaScript.Indicators;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace NinjaTrader.NinjaScript.Strategies;

public class StrategyAnalyzerExporter : Strategy
{
    public const int PRIMARY_SERIES = 0;

    private int _parsedTimeStart;
    private int _parsedTimeEnd;
    private FeaturesBarService _featuresBarService;
    private EventManager _eventManager;
    private ExporterDatabaseManager _databaseManager;

    private EMA _movingAverage;
    private EMA _slowMovingAverage;

    // ---- Historical timing fields ----
    private Stopwatch _histStopwatch;
    private bool _histStarted;
    private bool _histEnded;
    private long _histProcessedBars;

    #region Properties

    public const string GROUP_NAME_DEFAULT = "1. Strategy Analyzer Exporter";

    [NinjaScriptProperty]
    [Display(Name = "Enable Write to Database", Description = "Enable to write to database.", Order = 0, GroupName = GROUP_NAME_DEFAULT)]
    public bool EnableWriteToDatabase { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Enable Print Data Bar in Output", Description = "Enable to the feature bar in the output.", Order = 1, GroupName = GROUP_NAME_DEFAULT)]
    public bool EnablePrintDataBar { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Database Path", Description = "The database path.", Order = 2, GroupName = GROUP_NAME_DEFAULT)]
    public string DatabasePath { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Use Float32", Description = "Use float32 instead of double when writing to the database. Less precision and size reduces approximately 50%.", Order = 3, GroupName = GROUP_NAME_DEFAULT)]
    public bool UseFloat32 { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Table Name", Description = "The table name.", Order = 4, GroupName = GROUP_NAME_DEFAULT)]
    public string TableName { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Time Enabled", Description = "Enable this to enable time start/end.", Order = 5, GroupName = GROUP_NAME_DEFAULT)]
    public bool TimeEnabled { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Time Start", Description = "The allowed time to enable.", Order = 6, GroupName = GROUP_NAME_DEFAULT)]
    public string TimeStart { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Time End", Description = "The allowed time to disable and close positions.", Order = 7, GroupName = GROUP_NAME_DEFAULT)]
    public string TimeEnd { get; set; }

    #endregion

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Description = @"Exports the historical bar data using the strategy analyzer with maximum performance.";
            Name = "_StrategyAnalyzerExporter";
            Calculate = Calculate.OnBarClose;
            EntriesPerDirection = 1;
            EntryHandling = EntryHandling.AllEntries;
            IsExitOnSessionCloseStrategy = true;
            ExitOnSessionCloseSeconds = 30;
            IsFillLimitOnTouch = false;
            MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
            OrderFillResolution = OrderFillResolution.Standard;
            Slippage = 0;
            StartBehavior = StartBehavior.WaitUntilFlat;
            TraceOrders = false;
            RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
            StopTargetHandling = StopTargetHandling.PerEntryExecution;
            BarsRequiredToTrade = 20;
            IsInstantiatedOnEachOptimizationIteration = true;

            // Properties (DB off by default for timing runs)
            EnableWriteToDatabase = false;   // << set false to measure calc-only
            EnablePrintDataBar = false;
            DatabasePath = @"C:\temp\features.duckdb";
            TableName = "Features";
            UseFloat32 = true;
            TimeEnabled = true;
            TimeStart = "090000";
            TimeEnd = "155500";
        }
        else if (State == State.DataLoaded)
        {
            var config = new StrategyAnalyzerExporterConfig
            {
                EnableWriteToDatabase = EnableWriteToDatabase,
                EnablePrintDataBar = EnablePrintDataBar,
                DatabasePath = DatabasePath,
                UseFloat32 = UseFloat32,
                TableName = TableName,

                // Optimized batch settings for better performance
                FlushSize = 50_000,      // Larger batches for better throughput
                FlushIntervalSeconds = 60,

                // More aggressive commit settings for real-time performance
                CommitEveryRows = 10_000,  // Smaller commits for lower latency
                MaxTxDurationSeconds = 30,  // Prevent long-running transactions
                IdleTailCommitSeconds = 15, // Quick commits when idle
                CheckpointEveryCommits = 10, // More frequent checkpoints
            };

            var featuresBarConfig = new FeaturesBarConfig
            {
                TickSize = TickSize,
                BarsRequiredToTrade = BarsRequiredToTrade,
                LookbackPeriod = 9,
                LookbackPeriodSlow = 14,
            };

            _parsedTimeStart = int.Parse(TimeStart);
            _parsedTimeEnd = int.Parse(TimeEnd);
            _featuresBarService = new FeaturesBarService(featuresBarConfig);
            _eventManager = new EventManager();

            // Only create DB manager if writing is enabled
            if (EnableWriteToDatabase)
                _databaseManager = new ExporterDatabaseManager(config, _eventManager);

            // Indicators
            _movingAverage = EMA(BarsArray[PRIMARY_SERIES], 9);
            _slowMovingAverage = EMA(BarsArray[PRIMARY_SERIES], 21);

            // Timing init
            _histStopwatch = new Stopwatch();
            _histStarted = false;
            _histEnded = false;
            _histProcessedBars = 0;

            _eventManager.OnPrintMessage += HandlePrintMessage;
        }
        else if (State == State.Terminated)
        {
            try
            {
                // If DB was enabled, finish cleanly
                if (EnableWriteToDatabase)
                {
                    _databaseManager?.FlushPending();
                    _databaseManager?.FinalizeAndClose();
                }

                // If historical never transitioned (rare), print any timing we have
                if (_histStarted && !_histEnded)
                {
                    _histStopwatch.Stop();
                    var secs = System.Math.Max(0.0001, _histStopwatch.Elapsed.TotalSeconds);
                    var rate = _histProcessedBars / secs;
                    HandlePrintMessage(
                        $"Calculation finished (termination): {_histProcessedBars:N0} bars in {_histStopwatch.Elapsed.TotalSeconds:N1}s ({rate:N0} bars/s)."
                    );
                }
            }
            finally
            {
                _eventManager.OnPrintMessage -= HandlePrintMessage;
                _databaseManager = null;
            }
        }
    }

    protected override void OnBarUpdate()
    {
        if (BarsInProgress == 0) HandlePrimaryBar();
    }

    private void HandlePrimaryBar()
    {
        if (CurrentBars[PRIMARY_SERIES] < BarsRequiredToTrade) return;

        bool shouldProcess = true;
        if (TimeEnabled)
        {
            int barTime = ToTime(Times[PRIMARY_SERIES][1]);
            shouldProcess = barTime >= _parsedTimeStart && barTime <= _parsedTimeEnd;
        }
        if (!shouldProcess) return;

        // Historical timing start
        bool isHistorical = State == State.Historical;
        if (isHistorical && !_histStarted)
        {
            _histStarted = true;
            _histStopwatch.Start();
        }

        FeaturesBar? bar = _featuresBarService.GetFeaturesBar(
            new BaseBar
            {
                Time = ToTime(Times[PRIMARY_SERIES][1]),
                Day = ToDay(Times[PRIMARY_SERIES][1]),
                Open = Opens[PRIMARY_SERIES][1],
                High = Highs[PRIMARY_SERIES][1],
                Low = Lows[PRIMARY_SERIES][1],
                Close = Closes[PRIMARY_SERIES][1],
                Volume = Volumes[PRIMARY_SERIES][1],
                MovingAverage = _movingAverage[1],
                SlowMovingAverage = _slowMovingAverage[1],
            }
        );

        if (bar == null) return;

        if (EnablePrintDataBar)
            HandlePrintMessage(
                $"t={bar.Value.Time}, d={bar.Value.Day}, O={bar.Value.Open}, H={bar.Value.High}, L={bar.Value.Low}, C={bar.Value.Close}, Vol={bar.Value.Volume}",
                true
            );

        // Count processed historical bars
        if (isHistorical) _histProcessedBars++;

        // Skip DB writes entirely if disabled
        if (EnableWriteToDatabase)
            _databaseManager?.OnNewBarAvailable(bar.Value);

        // On first realtime bar after historical, print timing once
        if (!isHistorical && _histStarted && !_histEnded)
        {
            _histStopwatch.Stop();
            _histEnded = true;

            var secs = System.Math.Max(0.0001, _histStopwatch.Elapsed.TotalSeconds);
            var rate = _histProcessedBars / secs;

            HandlePrintMessage(
                $"Calculation finished: {_histProcessedBars:N0} bars in {_histStopwatch.Elapsed.TotalSeconds:N1}s ({rate:N0} bars/s)."
            );
        }
    }

    private void HandlePrintMessage(string message, bool addNewLine = false)
    {
        Print(message);
        if (addNewLine) Print("");
    }
}
