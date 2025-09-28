namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter;

public class StrategyAnalyzerExporterConfig
{
    public bool EnableWriteToDatabase { get; set; }
    public bool EnablePrintDataBar { get; set; }
    public string DatabasePath { get; set; }
    public string TableName { get; set; }
    public bool UseFloat32 { get; set; } // Map double -> REAL(float32)

    public int FlushSize { get; set; }              // Rows buffered before sending to appender
    public int FlushIntervalSeconds { get; set; }   // Time cap between flushes

    public long CommitEveryRows { get; set; }       // Commit after N rows (e.g., 25_000). 0 = disabled
    public int MaxTxDurationSeconds { get; set; }   // Optional time cap for a TX (0 = disabled)
    public int IdleTailCommitSeconds { get; set; }  // Commit if idle this long (0 = disabled)
    public int CheckpointEveryCommits { get; set; } // Run checkpoint after N commits (0 = never)
}
