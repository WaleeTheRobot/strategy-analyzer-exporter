namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class DataBarsConfig
    {
        public bool EnableWriteToDatabase { get; set; }
        public bool EnablePrintDataBar { get; set; }
        public string DatabasePath { get; set; }
        public int TicksPerLevel { get; set; }
    }
}
