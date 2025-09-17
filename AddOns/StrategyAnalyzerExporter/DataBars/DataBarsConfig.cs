namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class DataBarsConfig
    {
        public bool EnableWriteToDatabase { get; set; }
        public bool EnablePrintDataBar { get; set; }
        public string DatabasePath { get; set; }
        public int TicksPerLevel { get; set; }
        public int LookbackPeriod { get; set; }
        public string TableName { get; set; }
        public bool UseFloat32 { get; set; } // Map double -> REAL(float32)
        public int FlushSize { get; set; } // Batch inserts
        public int FlushIntervalSeconds { get; set; }
    }
}
