using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoAssemblyAddin.Services;
using AutoAssemblyAddin.UI;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorks.Interop.swconst;

namespace AutoAssemblyAddin;

[Guid(ComRegistration.AddinGuid)]
[ComVisible(true)]
[ProgId(ComRegistration.ProgId)]
public class SwAddin : ISwAddin
{
    // Use a unique ID to avoid clashing with other add-ins.
    private const int MainCmdGroupId = 128934;
    private const int ExportUserId = 0;
    private const int RebuildUserId = 1;
    private const int AssistantUserId = 2;
    private const string TabTitle = "AutoAssembly";

    private ISldWorks? _app;
    private int _addinCookie;
    private ICommandManager? _commandManager;
    private ICommandGroup? _commandGroup;
    private readonly List<CommandTab> _commandTabs = new();
    private TaskpaneView? _taskPane;
    private AssistantPane? _assistantPane;
    private AssistantController? _assistantController;

    public bool ConnectToSW(object thisSw, int cookie)
    {
        try
        {
            _app = (ISldWorks)thisSw;
            _addinCookie = cookie;
            _app.SetAddinCallbackInfo(0, this, _addinCookie);

            AddCommandManager();
            AddToolsMenuFallback();
            CreateAssistantPane();

            Log("ConnectToSW succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"ConnectToSW failed: {ex}");
            DisconnectFromSW();
            MessageBox.Show(
                $"Auto Assembly Pose 加载失败：{ex.Message}",
                "Auto Assembly Pose",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    public bool DisconnectFromSW()
    {
        RemoveAssistantPane();
        try
        {
            RemoveToolsMenuFallback();

            if (_commandManager != null)
            {
                foreach (var tab in _commandTabs)
                {
                    _commandManager.RemoveCommandTab(tab);
                }

                _commandManager.RemoveCommandGroup(MainCmdGroupId);
                Marshal.ReleaseComObject(_commandManager);
            }
        }
        catch (Exception ex)
        {
            Log($"DisconnectFromSW error: {ex}");
        }
        finally
        {
            _commandTabs.Clear();
            _commandGroup = null;
            _commandManager = null;
            _app = null;
        }

        return true;
    }

    public void ShowAssistant()
    {
        try
        {
            if (_taskPane == null) CreateAssistantPane();
            _taskPane?.ShowView();
            _assistantController?.RefreshContext();
        }
        catch (Exception ex) { ShowError("装配助手", ex); }
    }

    private void CreateAssistantPane()
    {
        if (_app == null) return;
        _assistantPane = new AssistantPane();
        _taskPane = _app.CreateTaskpaneView3(TaskPaneImages.Create(), "AutoAssembly 装配助手");
        if (_taskPane == null || !_taskPane.DisplayWindowFromHandlex64(_assistantPane.Handle.ToInt64()))
            throw new InvalidOperationException("无法创建 SolidWorks 右侧装配助手窗口。");
        _assistantController = new AssistantController(_app, _assistantPane, Log);
        _taskPane.ShowView();
    }

    private void RemoveAssistantPane()
    {
        try { _assistantController?.Dispose(); }
        catch (Exception ex) { Log("Assistant controller cleanup: " + ex); }
        _assistantController = null;
        try { _taskPane?.DeleteView(); }
        catch (Exception ex) { Log("Task pane cleanup: " + ex); }
        try { if (_taskPane != null) Marshal.ReleaseComObject(_taskPane); }
        catch (Exception ex) { Log("Task pane COM cleanup: " + ex); }
        _taskPane = null;
        try { _assistantPane?.Dispose(); }
        catch (Exception ex) { Log("Assistant control cleanup: " + ex); }
        _assistantPane = null;
    }

    public void ExportPose()
    {
        ExecuteCommand("Export Pose", service => service.ExportCurrentAssembly(), "位姿已导出到：");
    }

    public void RebuildPose()
    {
        if (_app == null)
        {
            return;
        }

        try
        {
            var jsonPath = PromptForJsonFile();
            if (jsonPath == null || string.IsNullOrWhiteSpace(jsonPath))
            {
                return;
            }

            var service = new PoseService(_app);
            var result = service.RebuildFromJson(jsonPath);
            _app.SendMsgToUser2(
                result.BuildMessage(),
                result.HasSkipped
                    ? (int)swMessageBoxIcon_e.swMbWarning
                    : (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
        }
        catch (Exception ex)
        {
            ShowError("Rebuild Pose", ex);
        }
    }

    public int EnableExportPose() => 1;

    public int EnableRebuildPose() => 1;

    private void ExecuteCommand(string title, Func<PoseService, string> action, string successPrefix)
    {
        if (_app == null)
        {
            return;
        }

        try
        {
            var service = new PoseService(_app);
            var outputPath = action(service);
            _app.SendMsgToUser2(
                $"{successPrefix}{outputPath}",
                (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
        }
        catch (Exception ex)
        {
            ShowError(title, ex);
        }
    }

    private string? PromptForJsonFile()
    {
        var initialDirectory = GetInitialJsonDirectory();

        using var dialog = new OpenFileDialog
        {
            Title = "选择位姿 JSON 文件",
            Filter = "Pose JSON (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    private string GetInitialJsonDirectory()
    {
        var model = (ModelDoc2?)_app?.ActiveDoc;
        if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        }

        var assemblyPath = model.GetPathName();
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        }

        return OutputPaths.GetOutputDirectory(assemblyPath);
    }

    private void ShowError(string title, Exception ex)
    {
        Log($"{title} error: {ex}");
        _app?.SendMsgToUser2(
            $"{title} 失败：{ex.Message}",
            (int)swMessageBoxIcon_e.swMbStop,
            (int)swMessageBoxBtn_e.swMbOk);
    }

    private void AddCommandManager()
    {
        if (_app == null)
        {
            throw new InvalidOperationException("SolidWorks 实例无效。");
        }

        _commandManager = _app.GetCommandManager(_addinCookie);
        var ignorePrevious = true; // Upgrade the persisted two-command toolbar to include the assistant.
        var cmdGroupErr = 0;

        _commandGroup = _commandManager.CreateCommandGroup2(
            MainCmdGroupId,
            TabTitle,
            "Auto Assembly Pose",
            "装配助手、模组替换与位姿导出/重建",
            -1,
            ignorePrevious,
            ref cmdGroupErr);

        if (_commandGroup == null)
        {
            throw new InvalidOperationException($"CreateCommandGroup2 失败，错误码 {cmdGroupErr}。");
        }

        var menuToolbarOption = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);

        var assistantIndex = _commandGroup.AddCommandItem2(
            "装配助手", -1, "打开右侧对话与模组替换窗口", "装配助手", 2,
            nameof(ShowAssistant), string.Empty, AssistantUserId, menuToolbarOption);

        var exportIndex = _commandGroup.AddCommandItem2(
            "Export Pose",
            -1,
            "导出第一层子装配体位姿到 JSON",
            "Export Pose",
            0,
            nameof(ExportPose),
            string.Empty,
            ExportUserId,
            menuToolbarOption);

        var rebuildIndex = _commandGroup.AddCommandItem2(
            "Rebuild Pose",
            -1,
            "从 JSON 重建装配体",
            "Rebuild Pose",
            1,
            nameof(RebuildPose),
            string.Empty,
            RebuildUserId,
            menuToolbarOption);

        _commandGroup.HasToolbar = true;
        _commandGroup.HasMenu = true;
        _commandGroup.Activate();

        AddCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, assistantIndex, exportIndex, rebuildIndex);
        AddCommandTab((int)swDocumentTypes_e.swDocPART, assistantIndex, exportIndex, rebuildIndex);
    }

    private void AddCommandTab(int docType, int assistantIndex, int exportIndex, int rebuildIndex)
    {
        if (_commandManager == null || _commandGroup == null)
        {
            return;
        }

        var existingTab = _commandManager.GetCommandTab(docType, TabTitle);
        if (existingTab != null)
        {
            _commandManager.RemoveCommandTab(existingTab);
        }

        var commandTab = (CommandTab?)_commandManager.AddCommandTab(docType, TabTitle);
        if (commandTab == null)
        {
            Log($"AddCommandTab failed for docType {docType}.");
            return;
        }

        var commandBox = commandTab.AddCommandTabBox();
        var commandIds = new[]
        {
            _commandGroup.CommandID[assistantIndex],
            _commandGroup.CommandID[exportIndex],
            _commandGroup.CommandID[rebuildIndex]
        };
        var textStyles = new[]
        {
            (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
            (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
            (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow
        };

        commandBox.AddCommands(commandIds, textStyles);
        _commandTabs.Add(commandTab);
    }

    private void AddToolsMenuFallback()
    {
        if (_app == null)
        {
            return;
        }

        var emptyIcons = Array.Empty<string>();
        _app.AddMenuItem5(
            (int)swDocumentTypes_e.swDocASSEMBLY, _addinCookie,
            "装配助手@Auto Assembly", 2, nameof(ShowAssistant), string.Empty,
            "打开右侧对话与模组替换窗口", emptyIcons);
        _app.AddMenuItem5(
            (int)swDocumentTypes_e.swDocASSEMBLY,
            _addinCookie,
            "Export Pose@Auto Assembly",
            0,
            nameof(ExportPose),
            nameof(EnableExportPose),
            "导出第一层子装配体位姿",
            emptyIcons);

        _app.AddMenuItem5(
            (int)swDocumentTypes_e.swDocASSEMBLY,
            _addinCookie,
            "Rebuild Pose@Auto Assembly",
            1,
            nameof(RebuildPose),
            nameof(EnableRebuildPose),
            "从 JSON 重建装配体",
            emptyIcons);
    }

    private void RemoveToolsMenuFallback()
    {
        if (_app == null)
        {
            return;
        }

        _app.RemoveMenu((int)swDocumentTypes_e.swDocASSEMBLY, "Auto Assembly", string.Empty);
    }

    private static void Log(string message)
    {
        try
        {
            var logDirectory = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "AutoAssemblyAddin");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "addin.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {message}{System.Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
