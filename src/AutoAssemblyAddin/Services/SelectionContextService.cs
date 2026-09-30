using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AutoAssemblyAddin.Services;

internal sealed class SelectionContext
{
    public ModelDoc2 Document { get; set; } = null!;
    public byte[] PersistentReference { get; set; } = Array.Empty<byte>();
    public string AssemblyPath { get; set; } = string.Empty;
    public string AssemblyConfiguration { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string ComponentPath { get; set; } = string.Empty;
    public string ComponentConfiguration { get; set; } = string.Empty;
}

internal sealed class SelectionContextService
{
    private readonly ISldWorks _app;
    public SelectionContextService(ISldWorks app) => _app = app;

    public ModelDoc2 GetAssembly()
    {
        var model = _app.ActiveDoc as ModelDoc2;
        if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            throw new InvalidOperationException("请先打开并激活一个装配体。");
        return model;
    }

    public string Describe()
    {
        var model = _app.ActiveDoc as ModelDoc2;
        if (model == null) return "未打开文档\r\n请打开装配体，点击模组上的面或在树中选择模组。";
        if (model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            return $"当前：{model.GetTitle()}\r\n请切换到装配体文档。";
        var manager = (SelectionMgr)model.SelectionManager;
        var count = manager.GetSelectedObjectCount2(-1);
        var prefix = $"装配：{model.GetTitle()}\r\n";
        if (count == 0) return prefix + "未选中模组 · 点击模组上的面或树中单选";
        if (count != 1) return prefix + $"已选择 {count} 个对象 · 替换需要单选";
        try
        {
            var clicked = manager.GetSelectedObjectsComponent4(1, -1) as Component2;
            var component = GetSingleComponent(model);
            var note = clicked != null && !SameObject(clicked, component)
                ? $"点击来源：{clicked.Name2}\r\n已识别所属顶层模组\r\n" : string.Empty;
            var state = component.GetSuppression2();
            var status = state == (int)swComponentSuppressionState_e.swComponentSuppressed ? "已抑制" :
                state == (int)swComponentSuppressionState_e.swComponentLightweight || state == (int)swComponentSuppressionState_e.swComponentFullyLightweight
                    ? "轻化（操作时自动解析）" : "已解析";
            return prefix + $"目标模组：{component.Name2}\r\n状态：{status}{(component.IsVirtual ? " · 虚拟模组" : "")}\r\n" + note + $"配置：{component.ReferencedConfiguration}\r\n文件：{component.GetPathName()}";
        }
        catch (InvalidOperationException ex) { return prefix + ex.Message; }
    }

    public SelectionContext Capture()
    {
        var model = GetAssembly();
        var component = GetSingleComponent(model);
        // Resolve only on an explicit operation, never during selection polling.
        var state = component.GetSuppression2();
        if (state == (int)swComponentSuppressionState_e.swComponentLightweight ||
            state == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
        {
            var edit = ((AssemblyDoc)model).GetEditTargetComponent();
            if (model.IsOpenedReadOnly() || (edit != null && !edit.IsRoot()))
                throw new InvalidOperationException("请返回可编辑的总装环境后再解析模组。");
            var identity = model.Extension.GetPersistReference3(component) as byte[];
            if (identity == null || identity.Length == 0) throw new InvalidOperationException("无法记录轻化模组实例，未解析。");
            component.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);
            component = Resolve(model, identity) ?? throw new InvalidOperationException("解析后未能重新定位模组，请重新选择。");
            if (!component.Select4(false, null, false)) throw new InvalidOperationException("解析后未能重新选择模组。");
        }
        ValidateComponent(model, component);
        var reference = model.Extension.GetPersistReference3(component) as byte[];
        if (reference == null || reference.Length == 0)
            throw new InvalidOperationException("无法记录该实例，请重新选择模组。");
        return new SelectionContext
        {
            Document = model,
            PersistentReference = reference,
            AssemblyPath = model.GetPathName(),
            AssemblyConfiguration = model.ConfigurationManager.ActiveConfiguration.Name,
            InstanceName = component.Name2,
            ComponentPath = component.GetPathName(),
            ComponentConfiguration = component.ReferencedConfiguration
        };
    }

    public Component2 ValidateCurrent(SelectionContext context)
    {
        var model = GetAssembly();
        if (!SameObject(model, context.Document) || !SamePath(model.GetPathName(), context.AssemblyPath) ||
            model.ConfigurationManager.ActiveConfiguration.Name != context.AssemblyConfiguration)
            throw new InvalidOperationException("装配体或配置已改变，请重新输入“替换该模组”。");
        var resolved = Resolve(model, context.PersistentReference);
        if (resolved == null || !SameObject(resolved, GetSingleComponent(model)))
            throw new InvalidOperationException("选中实例已改变或失效，请重新生成替换计划。");
        ValidateComponent(model, resolved);
        if (resolved.Name2 != context.InstanceName || !SamePath(resolved.GetPathName(), context.ComponentPath) ||
            resolved.ReferencedConfiguration != context.ComponentConfiguration)
            throw new InvalidOperationException("原模组的名称、文件或配置已改变，请重新生成替换计划。");
        return resolved;
    }

    private static Component2 GetSingleComponent(ModelDoc2 model)
    {
        var manager = (SelectionMgr)model.SelectionManager;
        if (manager.GetSelectedObjectCount2(-1) != 1)
            throw new InvalidOperationException("请单选模组上的面、边、顶点或组件，不要多选。");
        var type = manager.GetSelectedObjectType3(1, -1);
        if (type != (int)swSelectType_e.swSelCOMPONENTS && type != (int)swSelectType_e.swSelFACES &&
            type != (int)swSelectType_e.swSelEDGES && type != (int)swSelectType_e.swSelVERTICES)
            throw new InvalidOperationException("请点击模组上的面、边、顶点，或选择组件。");
        var component = manager.GetSelectedObjectsComponent4(1, -1) as Component2
            ?? throw new InvalidOperationException("当前对象不属于装配组件，请点击模组上的面。");
        for (var depth = 0; depth < 128; depth++)
        {
            var parent = component.GetParent();
            if (parent == null || parent.IsRoot())
            {
                if (component.IsRoot() || !string.Equals(Path.GetExtension(component.GetPathName()), ".sldasm", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("点击的零件不属于顶层子装配体模组，请选择模组内的零件。");
                return component;
            }
            component = parent;
        }
        throw new InvalidOperationException("组件层级异常，无法识别所属模组。");
    }

    private static void ValidateComponent(ModelDoc2 model, Component2 component)
    {
        var assembly = (AssemblyDoc)model;
        // SW also returns a non-null ROOT component while editing the top-level assembly.
        // Only a non-root edit target means in-context part/subassembly editing.
        var editTarget = assembly.GetEditTargetComponent();
        if (editTarget != null && !editTarget.IsRoot())
            throw new InvalidOperationException("请先退出零件/子装配体编辑，返回总装环境。");
        if (component.GetParent() is Component2 parent && !parent.IsRoot())
            throw new InvalidOperationException("当前版本只替换顶层模组，请选择总装下一层的子装配体。");
        if (component.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed)
            throw new InvalidOperationException("目标模组在当前配置中被抑制，请先在 SW 中解除抑制。");
        if (component.IsVirtual)
            throw new InvalidOperationException("目标是保存在装配体内部的虚拟模组，请先在 SW 中将模组保存为外部 SLDASM 文件，再替换。");
        var source = component.GetModelDoc2() as ModelDoc2;
        if (source == null)
            throw new InvalidOperationException("模组尚未完全解析，请在 SW 中将它设为完全解析后重试。");
        if (source.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY ||
            !string.Equals(Path.GetExtension(component.GetPathName()), ".sldasm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前版本的模组替换需要选择 SLDASM 子装配体。");
        if (model.IsOpenedReadOnly())
            throw new InvalidOperationException("当前装配体为只读，请以可编辑方式打开。");
    }

    internal static Component2? Resolve(ModelDoc2 model, byte[] reference)
    {
        var value = model.Extension.GetObjectByPersistReference3(reference, out var error);
        if (error != 0) return null;
        return value as Component2 ?? (value as Feature)?.GetSpecificFeature2() as Component2;
    }

    public void PromoteSelection()
    {
        var model = _app.ActiveDoc as ModelDoc2;
        if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) return;
        // Do not interfere with mate/property commands or in-context editing.
        if (!string.IsNullOrEmpty(model.Extension.GetActivePropertyManagerPage())) return;
        var edit = ((AssemblyDoc)model).GetEditTargetComponent();
        if (edit != null && !edit.IsRoot()) return;
        var manager = (SelectionMgr)model.SelectionManager;
        if (manager.GetSelectedObjectCount2(-1) != 1) return;
        var type = manager.GetSelectedObjectType3(1, -1);
        if (type != (int)swSelectType_e.swSelFACES && type != (int)swSelectType_e.swSelEDGES &&
            type != (int)swSelectType_e.swSelVERTICES) return;
        Component2 target;
        try { target = GetSingleComponent(model); }
        catch (InvalidOperationException) { return; }
        // Select4(false) replaces the face selection without a separate clear.
        if (target.Select4(false, null, false)) model.GraphicsRedraw2();
    }

    internal static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static bool SameObject(object left, object right)
    {
        if (ReferenceEquals(left, right)) return true;
        var first = Marshal.GetIUnknownForObject(left);
        try
        {
            var second = Marshal.GetIUnknownForObject(right);
            try { return first == second; }
            finally { Marshal.Release(second); }
        }
        finally { Marshal.Release(first); }
    }
}

