using System;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter;

public class EventManager
{
    public event Action<string, bool> OnPrintMessage;

    public void PrintMessage(string eventMessage, bool addNewLine = false)
    {
        try { OnPrintMessage?.Invoke(eventMessage, addNewLine); }
        catch (Exception ex) { OnPrintMessage?.Invoke($"Error printing message: {ex.Message}", true); }
    }
}
