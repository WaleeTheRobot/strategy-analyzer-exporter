#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class StrategyAnalyzerExporter : Strategy
    {
        public const int PRIMARY_SERIES = 0;
        public const int MINUTE_SERIES = 1;
        public const int SECONDARY_SERIES = 2;
        public const int TERTIARY_SERIES = 3;

        private DataBarsManager _dataBarsManager;
        private int _parsedTimeStart;
        private int _parsedTimeEnd;
        private bool _timeWindowOpen;

        #region Indicators

        private EMA _primaryMovingAverage;
        private EMA _secondaryMovingAverage;
        private EMA _tertiaryMovingAverage;

        #endregion

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

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Ticks Per Level", Description = "The ticks per level", Order = 3, GroupName = GROUP_NAME_DEFAULT)]
        public int TicksPerLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Time Enabled", Description = "Enable this to enable time start/end.", Order = 4, GroupName = GROUP_NAME_DEFAULT)]
        public bool TimeEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Time Start", Description = "The allowed time to enable.", Order = 5, GroupName = GROUP_NAME_DEFAULT)]
        public string TimeStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Time End", Description = "The allowed time to disable and close positions.", Order = 6, GroupName = GROUP_NAME_DEFAULT)]
        public string TimeEnd { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Exports the historical bar data using the strategy analyzer.";
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
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Properties
                EnableWriteToDatabase = true;
                EnablePrintDataBar = true;
                DatabasePath = @"C:\temp\strategy_analyzer_export.sqlite";
                TicksPerLevel = 1;
                TimeEnabled = true;
                TimeStart = "090000";
                TimeEnd = "155500";
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1); // Used for time range
                AddDataSeries(BarsPeriodType.Minute, 5);
                AddDataSeries(BarsPeriodType.Minute, 15); // Should be largest
            }
            else if (State == State.DataLoaded)
            {
                var config = new DataBarsConfig
                {
                    EnableWriteToDatabase = EnableWriteToDatabase,
                    EnablePrintDataBar = EnablePrintDataBar,
                    DatabasePath = DatabasePath,
                    TicksPerLevel = TicksPerLevel,
                };

                _dataBarsManager = new DataBarsManager(config);
                _parsedTimeStart = int.Parse(TimeStart);
                _parsedTimeEnd = int.Parse(TimeEnd);

                // Indicators
                _primaryMovingAverage = EMA(BarsArray[PRIMARY_SERIES], 9);
                _secondaryMovingAverage = EMA(BarsArray[SECONDARY_SERIES], 9);
                _tertiaryMovingAverage = EMA(BarsArray[TERTIARY_SERIES], 9);

                EventManager.OnPrintMessage += HandlePrintMessage;
            }
            else if (State == State.Terminated)
            {
                try
                {
                    _dataBarsManager?.FinalizeAndClose();
                }
                finally
                {
                    EventManager.OnPrintMessage -= HandlePrintMessage;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                switch (BarsInProgress)
                {
                    case PRIMARY_SERIES:
                        HandlePrimaryBar();
                        break;
                    case SECONDARY_SERIES:
                        HandleSecondaryBar();
                        break;
                    case TERTIARY_SERIES:
                        HandleTertiaryBar();
                        break;
                    case MINUTE_SERIES:
                        HandleMinuteBar();
                        break;
                }

                HandleLastBarFlush();
            }
            catch (Exception ex)
            {
                Print($"[{Time[0]}] Unexpected error: {ex}");
            }
        }

        private void HandlePrimaryBar()
        {
            if (CurrentBars[PRIMARY_SERIES] < BarsRequiredToTrade) return;
            if (TimeEnabled && !_timeWindowOpen) return;

            if (IsFirstTickOfBar)
            {
                _dataBarsManager.OnNewPrimaryBarAvailable(
                    CurrentBars[TERTIARY_SERIES] > BarsRequiredToTrade,
                    new DataBar
                    {
                        Time = ToTime(Times[PRIMARY_SERIES][1]),
                        Day = ToDay(Times[PRIMARY_SERIES][1]),
                        Open = Opens[PRIMARY_SERIES][1],
                        High = Highs[PRIMARY_SERIES][1],
                        Low = Lows[PRIMARY_SERIES][1],
                        Close = Closes[PRIMARY_SERIES][1],
                        Volume = Volumes[PRIMARY_SERIES][1],
                        MovingAverage = _primaryMovingAverage[1]
                    }
                );
            }
        }

        private void HandleSecondaryBar()
        {
            if (CurrentBars[SECONDARY_SERIES] < BarsRequiredToTrade) return;
            if (TimeEnabled && !_timeWindowOpen) return;

            if (IsFirstTickOfBar)
            {
                _dataBarsManager.OnNewSecondaryBarAvailable(
                    new DataBar
                    {
                        Time = ToTime(Times[SECONDARY_SERIES][1]),
                        Day = ToDay(Times[SECONDARY_SERIES][1]),
                        Open = Opens[SECONDARY_SERIES][1],
                        High = Highs[SECONDARY_SERIES][1],
                        Low = Lows[SECONDARY_SERIES][1],
                        Close = Closes[SECONDARY_SERIES][1],
                        Volume = Volumes[SECONDARY_SERIES][1],
                        MovingAverage = _secondaryMovingAverage[1]
                    }
                );
            }
        }

        private void HandleTertiaryBar()
        {
            if (CurrentBars[TERTIARY_SERIES] < BarsRequiredToTrade) return;
            if (TimeEnabled && !_timeWindowOpen) return;

            if (IsFirstTickOfBar)
            {
                _dataBarsManager.OnNewTertiaryBarAvailable(
                    new DataBar
                    {
                        Time = ToTime(Times[TERTIARY_SERIES][1]),
                        Day = ToDay(Times[TERTIARY_SERIES][1]),
                        Open = Opens[TERTIARY_SERIES][1],
                        High = Highs[TERTIARY_SERIES][1],
                        Low = Lows[TERTIARY_SERIES][1],
                        Close = Closes[TERTIARY_SERIES][1],
                        Volume = Volumes[TERTIARY_SERIES][1],
                        MovingAverage = _tertiaryMovingAverage[1]
                    }
                );
            }
        }

        private void HandleMinuteBar()
        {
            if (!IsFirstTickOfBar) return;
            int hhmmss = ToTime(Times[MINUTE_SERIES][0]);
            _timeWindowOpen = hhmmss >= _parsedTimeStart && hhmmss < _parsedTimeEnd;
        }

        private void HandlePrintMessage(string message, bool addNewLine)
        {
            Print(message);
            if (addNewLine) Print("");
        }

        private void HandleLastBarFlush()
        {
            if (State == State.Historical
                && IsInStrategyAnalyzer
                && BarsInProgress == PRIMARY_SERIES
                && CurrentBar == BarsArray[PRIMARY_SERIES].Count - 1)
            {
                _dataBarsManager?.FlushPending();
                _dataBarsManager?.FinalizeAndClose();
            }
        }
    }
}
