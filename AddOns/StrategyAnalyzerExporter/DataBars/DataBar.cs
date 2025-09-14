namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class DataBar
    {
        public int Time { get; set; }
        public int Day { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        public double MovingAverage { get; set; }
    }
}
