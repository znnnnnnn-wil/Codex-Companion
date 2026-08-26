using System.Diagnostics;
using System.Windows.Automation;
using CodexCompanion.Bridge.Codex.Models;
using Microsoft.Extensions.Logging;

namespace CodexCompanion.Bridge.Codex.Desktop;

public sealed class SystemWindowsCodexUiDriver(ILogger<SystemWindowsCodexUiDriver> logger) : ICodexUiDriver
{
    private const string SidebarThreadClassMarker = "group relative cursor-interaction text-sm";
    private const string WorkspaceClassMarker = "group/cwd";
    private static readonly AttachmentRoute[] AttachmentRoutes =
    [
        // The current-thread Sources control uses the native common dialog and
        // remains stable even while another turn is rendering in Desktop.
        new(["附加文件或连接应用", "Attach files or connect apps"],
            ["添加文件或文件夹", "Add files or folders"]),
        // Newer composer builds expose a second semantic path. Keep it as a
        // fallback because some layouts hide the Sources panel.
        new(["添加文件等内容", "Add files and more"],
            ["文件和文件夹", "Files and folders"])
    ];

    public bool IsCodexRunning() => TryGetCodexWindow() is not null;

    public DesktopConversation? GetCurrentConversation()
    {
        var window = GetCodexWindow();
        var selected = GetSidebarThreads(window)
            .Where(candidate => candidate.IsSelected)
            .ToArray();
        return selected.Length == 1
            ? new DesktopConversation(selected[0].Title, selected[0].Workspace, true)
            : null;
    }

    public ConversationOpenResult CreateConversation(string cwd)
    {
        var window = GetCodexWindow();
        var workspace = GetWorkspaceName(cwd);
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return ConversationOpenResult.NotFound;
        }

        var names = new[]
        {
            $"在 {workspace} 中开始新聊天",
            $"Start a new chat in {workspace}",
            $"Start new chat in {workspace}"
        };
        var candidates = FindVisibleButtons(window)
            .Where(element => names.Contains(SafeCurrent(element, current => current.Name), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            logger.LogWarning("No Codex sidebar button matched new-chat action for workspace {Workspace}", workspace);
            return ConversationOpenResult.NotFound;
        }
        if (candidates.Length > 1)
        {
            logger.LogWarning("Multiple Codex new-chat buttons matched workspace {Workspace} ({Count})", workspace, candidates.Length);
            return ConversationOpenResult.Ambiguous;
        }
        if (!candidates[0].TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            throw new BridgeException(BridgeErrorCode.ThreadCreateFailed, "Codex 新建会话按钮不支持 InvokePattern。");
        }

        ((InvokePattern)pattern).Invoke();
        if (WaitForElement(() => FindComposer(window), TimeSpan.FromSeconds(5)) is null)
        {
            throw new BridgeException(BridgeErrorCode.ThreadCreateFailed, "Codex Desktop 未能打开新会话。");
        }
        return ConversationOpenResult.Opened;
    }

    public ConversationOpenResult OpenConversation(string title, string cwd)
    {
        var window = GetCodexWindow();
        var workspace = GetWorkspaceName(cwd);
        var titleMatches = GetSidebarThreads(window)
            .Where(candidate => string.Equals(candidate.Title, title, StringComparison.Ordinal))
            .ToArray();
        var candidates = string.IsNullOrWhiteSpace(workspace)
            ? titleMatches
            : titleMatches.Where(candidate =>
                string.Equals(candidate.Workspace, workspace, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (candidates.Length == 0)
        {
            logger.LogWarning("No Codex sidebar item matched title/workspace metadata");
            return ConversationOpenResult.NotFound;
        }

        if (candidates.Length > 1)
        {
            logger.LogWarning("Multiple Codex sidebar items matched title/workspace metadata ({Count})", candidates.Length);
            return ConversationOpenResult.Ambiguous;
        }

        var candidate = candidates[0];
        if (!candidate.IsSelected)
        {
            TryScrollIntoView(candidate.Element);
            if (!candidate.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                throw new BridgeException(BridgeErrorCode.CodexSendFailed, "Codex 会话按钮不支持 InvokePattern。");
            }

            ((InvokePattern)pattern).Invoke();
            WaitForConversation(title, workspace, TimeSpan.FromSeconds(5));
        }

        return ConversationOpenResult.Opened;
    }

    public void SetComposerText(string text)
    {
        var window = GetCodexWindow();
        var composer = FindComposer(window)
            ?? throw new BridgeException(BridgeErrorCode.CodexInputNotFound, "找不到 Codex 输入框。");
        if (!composer.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new BridgeException(
                BridgeErrorCode.CodexInputNotFound,
                "Codex 输入框未暴露 ValuePattern，当前版本无法安全输入。");
        }

        try
        {
            ((ValuePattern)pattern).SetValue(text);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
            throw new BridgeException(BridgeErrorCode.CodexInputNotFound, "无法通过 ValuePattern 写入 Codex 输入框。", exception);
        }
    }

    public void AttachFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }
        if (paths.Any(path => !File.Exists(path)))
        {
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "待上传的附件在电脑临时目录中不存在。");
        }
        // The Windows common dialog may reject a multi-select value when several
        // long absolute paths exceed its edit-control limit. Adding files one by
        // one uses the same semantic UIA flow and avoids that OS-specific limit.
        if (paths.Count > 1)
        {
            foreach (var path in paths)
            {
                AttachFiles([path]);
            }
            return;
        }

        var window = GetCodexWindow();
        var processId = window.Current.ProcessId;
        AutomationElement? dialog = null;
        foreach (var route in AttachmentRoutes)
        {
            dialog = TryOpenAttachmentDialog(window, processId, route);
            if (dialog is not null)
            {
                break;
            }
        }
        dialog ??= WaitForElement(() => FindFileDialog(processId), TimeSpan.FromMilliseconds(300));
        if (dialog is null)
        {
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "Codex 未打开文件选择窗口。");
        }
        var value = paths.Count == 1
            ? Path.GetFullPath(paths[0])
            : string.Join(' ', paths.Select(path => $"\"{Path.GetFullPath(path)}\""));
        if (!TrySetFileDialogValue(dialog, value))
        {
            TryCancelFileDialog(dialog);
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "无法写入文件选择窗口的文件名输入框。");
        }

        var openButton = dialog.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
        if (openButton is null || !openButton.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeOpenPattern))
        {
            TryCancelFileDialog(dialog);
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "文件选择窗口未暴露“打开”按钮。");
        }
        ((InvokePattern)invokeOpenPattern).Invoke();

        if (WaitForElement(() => FindFileDialog(processId), TimeSpan.FromSeconds(5), expectMissing: true) is not null)
        {
            TryCancelFileDialog(dialog);
            throw new BridgeException(BridgeErrorCode.CodexAttachmentFailed, "Codex 未能接收所选附件。");
        }
    }

    private static AutomationElement? TryOpenAttachmentDialog(
        AutomationElement window,
        int processId,
        AttachmentRoute route)
    {
        var attachButton = WaitForElement(
            () => FindVisibleButtons(window).FirstOrDefault(element => route.ButtonNames.Contains(
                SafeCurrent(element, current => current.Name), StringComparer.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(1));
        if (attachButton is null
            || !attachButton.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern))
        {
            return null;
        }

        var expand = (ExpandCollapsePattern)expandPattern;
        try
        {
            expand.Expand();
            var addFileItem = WaitForElement(
                () => FindVisibleButtons(window)
                    .Concat(FindVisibleMenuItems(window))
                    .FirstOrDefault(element => route.MenuNames.Contains(
                        SafeCurrent(element, current => current.Name), StringComparer.OrdinalIgnoreCase)),
                TimeSpan.FromSeconds(2));
            if (addFileItem is null
                || !addFileItem.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeMenuPattern))
            {
                return null;
            }
            ((InvokePattern)invokeMenuPattern).Invoke();
            return WaitForElement(() => FindFileDialog(processId), TimeSpan.FromSeconds(3));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException)
        {
            return null;
        }
        finally
        {
            if (FindFileDialog(processId) is null)
            {
                try { expand.Collapse(); } catch (InvalidOperationException) { }
                catch (ElementNotAvailableException) { }
            }
        }
    }

    public void InvokeSend()
    {
        var window = GetCodexWindow();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        AutomationElement? sendButton = null;
        while (DateTimeOffset.UtcNow < deadline && sendButton is null)
        {
            sendButton = FindVisibleButtons(window)
                .FirstOrDefault(element =>
                    string.Equals(SafeCurrent(element, current => current.Name), "发送", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SafeCurrent(element, current => current.Name), "Send", StringComparison.OrdinalIgnoreCase));
            if (sendButton is null)
            {
                Thread.Sleep(100);
            }
        }

        if (sendButton is null)
        {
            var working = FindVisibleButtons(window).Any(element =>
                string.Equals(SafeCurrent(element, current => current.Name), "停止", StringComparison.OrdinalIgnoreCase)
                || string.Equals(SafeCurrent(element, current => current.Name), "Stop", StringComparison.OrdinalIgnoreCase));
            throw new BridgeException(
                BridgeErrorCode.CodexSendFailed,
                working ? "Codex 正在工作，当前没有可用的发送按钮。" : "找不到 Codex 发送按钮。");
        }

        if (!sendButton.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            throw new BridgeException(BridgeErrorCode.CodexSendFailed, "Codex 发送按钮不支持 InvokePattern。");
        }
        ((InvokePattern)pattern).Invoke();
    }

    public void InvokeStop()
    {
        var window = GetCodexWindow();
        var stopButton = FindVisibleButtons(window).FirstOrDefault(element =>
            string.Equals(SafeCurrent(element, current => current.Name), "停止", StringComparison.OrdinalIgnoreCase)
            || string.Equals(SafeCurrent(element, current => current.Name), "Stop", StringComparison.OrdinalIgnoreCase));
        if (stopButton is null)
        {
            throw new BridgeException(BridgeErrorCode.CodexNotWorking, "该 Codex 会话当前没有正在执行的任务。");
        }
        if (!stopButton.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            throw new BridgeException(BridgeErrorCode.CodexStopFailed, "Codex 停止按钮不支持 InvokePattern。");
        }
        ((InvokePattern)pattern).Invoke();
    }

    public string GetState()
    {
        var window = GetCodexWindow();
        return FindVisibleButtons(window).Any(element =>
            string.Equals(SafeCurrent(element, current => current.Name), "停止", StringComparison.OrdinalIgnoreCase)
            || string.Equals(SafeCurrent(element, current => current.Name), "Stop", StringComparison.OrdinalIgnoreCase))
            ? "working"
            : "idle";
    }

    internal static AutomationElement? TryGetCodexWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            try
            {
                var executable = process.MainModule?.FileName ?? string.Empty;
                if (!executable.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                foreach (AutomationElement window in windows)
                {
                    WarmAccessibilityTree(window);
                    var documentCondition = new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "RootWebArea"));
                    var document = window.FindFirst(TreeScope.Descendants, documentCondition);
                    if (document is not null
                        && string.Equals(document.Current.Name, "Codex", StringComparison.OrdinalIgnoreCase))
                    {
                        return window;
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException)
            {
            }
        }
        return null;
    }

    internal static void WarmAccessibilityTree(AutomationElement window)
    {
        var descendants = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        if (descendants.Count < 50)
        {
            Thread.Sleep(350);
            window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        }
    }

    private static AutomationElement GetCodexWindow()
        => TryGetCodexWindow() ?? throw new BridgeException(BridgeErrorCode.CodexNotRunning, "电脑上的 Codex 当前未运行。");

    private static IReadOnlyList<SidebarThread> GetSidebarThreads(AutomationElement window)
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var results = new List<SidebarThread>();
        foreach (AutomationElement button in buttons)
        {
            try
            {
                var current = button.Current;
                if (!current.ClassName.Contains(SidebarThreadClassMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                var workspace = FindWorkspace(button);
                if (string.IsNullOrWhiteSpace(current.Name) || string.IsNullOrWhiteSpace(workspace))
                {
                    continue;
                }

                var selected = current.ClassName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("bg-primary-ghost-hover", StringComparer.Ordinal);
                results.Add(new SidebarThread(button, current.Name, workspace, selected));
            }
            catch (ElementNotAvailableException)
            {
            }
        }
        return results;
    }

    private static string FindWorkspace(AutomationElement element)
    {
        var walker = TreeWalker.RawViewWalker;
        var parent = walker.GetParent(element);
        while (parent is not null)
        {
            try
            {
                var current = parent.Current;
                if (current.ControlType == ControlType.Group
                    && current.ClassName.Contains(WorkspaceClassMarker, StringComparison.Ordinal))
                {
                    return current.Name;
                }
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
            parent = walker.GetParent(parent);
        }
        return string.Empty;
    }

    private static string GetWorkspaceName(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return string.Empty;
        }
        var normalized = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalized);
    }

    private static AutomationElement? FindComposer(AutomationElement window)
    {
        var edits = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        return edits.Cast<AutomationElement>().FirstOrDefault(element =>
            SafeCurrent(element, current => current.IsEnabled && !current.IsOffscreen
                                            && current.ClassName.Contains("ProseMirror", StringComparison.Ordinal)));
    }

    private static IEnumerable<AutomationElement> FindVisibleButtons(AutomationElement window)
    {
        return window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Where(element => SafeCurrent(element, current => current.IsEnabled && !current.IsOffscreen));
    }

    private static IEnumerable<AutomationElement> FindVisibleMenuItems(AutomationElement window)
    {
        return window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
            .Cast<AutomationElement>()
            .Where(element => SafeCurrent(element, current => current.IsEnabled && !current.IsOffscreen));
    }

    private static AutomationElement? FindFileDialog(int processId)
    {
        var windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId)));
        return windows.Cast<AutomationElement>().FirstOrDefault(window =>
            window.FindFirst(TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox))) is not null
            && window.FindFirst(TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) is not null);
    }

    private static void TryCancelFileDialog(AutomationElement dialog)
    {
        try
        {
            var cancel = dialog.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "2"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
            if (cancel?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true)
            {
                ((InvokePattern)pattern).Invoke();
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static bool TrySetFileDialogValue(AutomationElement dialog, string value)
    {
        foreach (var controlType in new[] { ControlType.ComboBox, ControlType.Edit })
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var input = dialog.FindFirst(
                        TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, controlType)));
                    if (input?.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) == true)
                    {
                        ((ValuePattern)pattern).SetValue(value);
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException)
                {
                    Thread.Sleep(100);
                }
            }
        }
        return false;
    }

    private static AutomationElement? WaitForElement(
        Func<AutomationElement?> lookup,
        TimeSpan timeout,
        bool expectMissing = false)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        AutomationElement? current = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            current = lookup();
            if ((!expectMissing && current is not null) || (expectMissing && current is null))
            {
                return current;
            }
            Thread.Sleep(100);
        }
        return current;
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var pattern))
        {
            ((ScrollItemPattern)pattern).ScrollIntoView();
        }
    }

    private static void WaitForConversation(string title, string workspace, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var window = TryGetCodexWindow();
            if (window is not null)
            {
                var selected = GetSidebarThreads(window).Where(candidate => candidate.IsSelected).ToArray();
                if (selected.Length == 1
                    && string.Equals(selected[0].Title, title, StringComparison.Ordinal)
                    && (string.IsNullOrWhiteSpace(workspace)
                        || string.Equals(selected[0].Workspace, workspace, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }
            Thread.Sleep(100);
        }

        throw new BridgeException(BridgeErrorCode.CodexSendFailed, "Codex Desktop 未确认会话切换。");
    }

    private static T SafeCurrent<T>(AutomationElement element, Func<AutomationElement.AutomationElementInformation, T> read)
    {
        try
        {
            return read(element.Current);
        }
        catch (ElementNotAvailableException)
        {
            return default!;
        }
    }

    private sealed record AttachmentRoute(string[] ButtonNames, string[] MenuNames);
    private sealed record SidebarThread(AutomationElement Element, string Title, string Workspace, bool IsSelected);
}
