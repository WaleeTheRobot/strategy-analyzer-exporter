using System;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter
{
    public static class EventManager
    {
        public static event Action<string, bool> OnPrintMessage;

        public static void PrintMessage(string eventMessage, bool addNewLine = false)
        {
            try { OnPrintMessage?.Invoke(eventMessage, addNewLine); }
            catch (Exception ex) { OnPrintMessage?.Invoke($"Error printing message: {ex.Message}", true); }
        }
    }
}
