using System.Text;
using System.Windows.Automation;
using CodexCompanion.Bridge.Codex.Models;

namespace CodexCompanion.Bridge.Codex.Desktop;

public sealed class CodexUiInspector
{
    public string Inspect(int maxDepth = 64, int maxNodes = 3_000)
    {
        var window = SystemWindowsCodexUiDriver.TryGetCodexWindow()
            ?? throw new BridgeException(BridgeErrorCode.CodexNotRunning, "电脑上的 Codex 当前未运行。");
        SystemWindowsCodexUiDriver.WarmAccessibilityTree(window);

        var builder = new StringBuilder();
        var count = 0;
        Dump(window, 0, maxDepth, maxNodes, builder, ref count);
        builder.AppendLine($"Nodes: {count}");
        return builder.ToString();
    }

    private static void Dump(
        AutomationElement element,
        int depth,
        int maxDepth,
        int maxNodes,
        StringBuilder output,
        ref int count)
    {
        if (depth > maxDepth || count >= maxNodes)
        {
            return;
        }

        try
        {
            var current = element.Current;
            output.Append(' ', depth * 2)
                .Append(current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.Ordinal))
                .Append(" Name=\"").Append(Trim(current.Name, 160)).Append('"')
                .Append(" AutomationId=\"").Append(Trim(current.AutomationId, 100)).Append('"')
                .Append(" ClassName=\"").Append(Trim(current.ClassName, 220)).Append('"')
                .Append(" IsEnabled=").Append(current.IsEnabled)
                .Append(" IsOffscreen=").Append(current.IsOffscreen)
                .AppendLine();
            count++;
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        var walker = TreeWalker.RawViewWalker;
        var child = walker.GetFirstChild(element);
        while (child is not null && count < maxNodes)
        {
            Dump(child, depth + 1, maxDepth, maxNodes, output, ref count);
            child = walker.GetNextSibling(child);
        }
    }

    private static string Trim(string? value, int length)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= length ? normalized : normalized[..length] + "…";
    }
}
