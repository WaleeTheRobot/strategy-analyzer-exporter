using System.Linq;
using System.Reflection;
using System.Text;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils
{
    public static class BarPrinter
    {
        public static void Print(object obj, string label = null)
        {
            if (obj == null)
            {
                EventManager.PrintMessage($"{label ?? "Object"}: null", true);
                return;
            }

            var type = obj.GetType();
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead);

            var sb = new StringBuilder();
            sb.AppendLine($"{label ?? type.Name}:");

            foreach (var p in props)
            {
                object val;
                try { val = p.GetValue(obj); }
                catch { val = "(unreadable)"; }

                sb.AppendLine($"{p.Name}:  {val}");
            }

            EventManager.PrintMessage(sb.ToString(), true);
        }
    }
}
