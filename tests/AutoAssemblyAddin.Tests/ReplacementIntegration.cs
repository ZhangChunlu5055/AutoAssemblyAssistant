using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics;
using System.Threading;
using AutoAssemblyAddin.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class ReplacementIntegration
{
    public static int Run(string outputRoot, bool testMotion = false)
    {
        // Never test mutations in the user's SW process, even if COM reuses a running server.
        ISldWorks? current = null;
        try { current = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (COMException) { }
        var originalPid = current?.GetProcessID() ?? -1;
        ISldWorks? app = null;
        Process? process = null;
        var ownsProcess = false;
        try
        {
            var executable = current != null ? Process.GetProcessById(originalPid).MainModule.FileName
                : (string?)Microsoft.Win32.Registry.GetValue(@"HKEY_CLASSES_ROOT\CLSID\{AFBEC3B2-B1A6-4908-B608-D97D2AAB5498}\LocalServer32", "", null);
            if (!File.Exists(executable)) throw new InvalidOperationException("Cannot locate SW executable.");
            process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            Console.WriteLine("Started dedicated SW PID=" + process!.Id);
            var deadline = DateTime.UtcNow.AddSeconds(55);
            while (app == null && DateTime.UtcNow < deadline && !process.HasExited)
            {
                app = FindProcess(process.Id);
                if (app == null) Thread.Sleep(500);
            }
            if (app == null) throw new InvalidOperationException("Dedicated SW instance did not become available within 55 seconds.");
            var pid = app.GetProcessID();
            if (pid == originalPid || app.GetDocumentCount() != 0)
                throw new InvalidOperationException("COM did not create an empty isolated SW instance; no test mutations performed.");
            ownsProcess = true;
            Console.WriteLine($"Isolated SW PID={pid}; original PID={originalPid}");
            var directory = Path.Combine(outputRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);

            var partTemplate = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart).Trim('"');
            if (!File.Exists(partTemplate))
                partTemplate = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                    "SOLIDWORKS", "SOLIDWORKS 2024", "templates", "gb_part.prtdot");
            Require(File.Exists(partTemplate), "Part template exists");
            var part = (ModelDoc2)app.NewDocument(partTemplate, 0, 0, 0);
            part.SketchManager.Insert3DSketch(true);
            part.SketchManager.CreateLine(0, 0, 0, .01, 0, 0);
            part.SketchManager.Insert3DSketch(true);
            var partPath = Path.Combine(directory, "Marker.SLDPRT");
            var body = ((Modeler)app.GetModeler()).CreateBodyFromBox(new double[] { 0, 0, 0, 0, 0, 1, .01, .01, .01 });
            Require(((PartDoc)part).CreateFeatureFromBody3(body, false, 0) != null, "Solid geometry created for face selection");
            Save(part, partPath);

            var source = AssemblyDocumentFactory.CreateNewAssembly(app);
            Require(((AssemblyDoc)source).AddComponent5(partPath, 0, "", false, "", 0, 0, 0) != null, "Source marker inserted");
            var sourcePath = Path.Combine(directory, "ModuleA.SLDASM");
            Save(source, sourcePath);
            var candidate = AssemblyDocumentFactory.CreateNewAssembly(app);
            Require(((AssemblyDoc)candidate).AddComponent5(partPath, 0, "", false, "", 0, 0, 0) != null, "Candidate marker inserted");
            candidate.ConfigurationManager.AddConfiguration("ReplacementTest", "", "", 0, "", "");
            candidate.ShowConfiguration2("ReplacementTest");
            var candidatePath = Path.Combine(directory, "ModuleB.SLDASM");
            Save(candidate, candidatePath);

            var model = AssemblyDocumentFactory.CreateNewAssembly(app);
            var assembly = (AssemblyDoc)model;
            var first = assembly.AddComponent5(sourcePath, 0, "", false, "", 0, 0, 0);
            var second = assembly.AddComponent5(sourcePath, 0, "", false, "", .05, 0, 0);
            Require(first != null && second != null, "Two instances of the same source inserted");
            var assemblyPath = Path.Combine(directory, "ReplacementFixture.SLDASM");
            Save(model, assemblyPath);
            // Setup may flag source documents when their configuration is resolved. Only test-owned documents are saved.
            foreach (var doc in ((object[])app.GetDocuments()).OfType<ModelDoc2>())
            {
                if (doc.GetSaveFlag())
                {
                    Require(doc.GetPathName().StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Only fixture files are saved");
                    var errors = 0;
                    var warnings = 0;
                    var ok = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                    Require(ok && errors == 0, "Fixture setup saved");
                }
            }
            Console.WriteLine("Edit target root=" + assembly.GetEditTargetComponent()?.IsRoot());
            var selection = new SelectionContextService(app);
            var replacement = new ModuleReplacementService(app, selection);
            model.ClearSelection2(true);
            Require(first!.Select4(false, null, false), "First instance selected");
            var context = selection.Capture();
            Require(selection.ValidateCurrent(context) != null, "Normal root edit target is accepted");
            SelectModuleFace(model, first);
            Require(selection.Capture().InstanceName == context.InstanceName, "Face resolves to owning top-level module");
            Require(selection.ValidateCurrent(context) != null, "Tree and face resolve to same instance");
            selection.PromoteSelection();
            Require(((SelectionMgr)model.SelectionManager).GetSelectedObjectType3(1, -1) == (int)swSelectType_e.swSelCOMPONENTS,
                "Face click promotes SW selection to whole module");
            Require(selection.Capture().InstanceName == context.InstanceName, "Promoted selection targets the correct module");
            app.CloseDoc(source.GetTitle());
            app.CloseDoc(part.GetTitle());
            first.SetSuppression2((int)swComponentSuppressionState_e.swComponentLightweight);
            Require(first.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentLightweight, "Fixture target is lightweight");
            Require(first.IsSuppressed(), "Reproduced legacy IsSuppressed lightweight false positive");
            Require(first.Select4(false, null, false), "Lightweight target selected");
            context = selection.Capture();
            Require(first.GetModelDoc2() != null && first.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentLightweight,
                "Explicit operation resolves lightweight target");
            var plan = replacement.Prepare(context, candidatePath);
            Require(plan.Configurations.Contains("ReplacementTest"), "Candidate configuration detected");

            model.ClearSelection2(true);
            SelectModuleFace(model, second!);
            ExpectRejection(() => selection.ValidateCurrent(context), "Changed selected instance rejected");
            model.ClearSelection2(true);
            SelectModuleFace(model, first);
            // In-context editing is an interactive SW command and is verified manually, not queued in a hidden instance.
            plan = replacement.Prepare(selection.Capture(), candidatePath);
            var result = replacement.Execute(plan, "ReplacementTest");
            Console.WriteLine(result);
            var components = ((object[])assembly.GetComponents(true)).OfType<Component2>().ToArray();
            Require(components.Length == 2, "Instance count unchanged");
            Require(components.Count(c => SelectionContextService.SamePath(c.GetPathName(), sourcePath)) == 1, "Unselected instance still references ModuleA");
            Require(components.Count(c => SelectionContextService.SamePath(c.GetPathName(), candidatePath) && c.ReferencedConfiguration == "ReplacementTest") == 1,
                "Selected instance references ModuleB and requested configuration");
            Require(result.StartsWith("已替换选中的一个模组实例。"), "Service verification reports success");
            File.WriteAllText(Path.Combine(directory, "result.txt"), result);
            if (testMotion) MotionIntegration.Run(app, model, candidatePath, directory);
            // Persist the fixture for inspection; production replacement service itself never saves.
            Save(model, assemblyPath);
            Console.WriteLine("PASS: isolated replacement integration. " + directory);
            return 0;
        }
        finally
        {
            if (ownsProcess && app != null) app.ExitApp();
            else if (process != null && !process.HasExited) process.CloseMainWindow();
            process?.Dispose();
        }
    }

    internal static void SelectModuleFace(ModelDoc2 model, Component2 module)
    {
        var child = ((object[])module.GetChildren()).OfType<Component2>().First();
        object info;
        var bodies = (object[])child.GetBodies3((int)swBodyType_e.swSolidBody, out info);
        var face = ((object[])((Body2)bodies[0]).GetFaces())[0];
        model.ClearSelection2(true);
        Require(((Entity)face).Select4(false, null), "Face in nested part selected in assembly context");
    }

    private static void Save(ModelDoc2 model, string path)
    {
        Require(!string.IsNullOrWhiteSpace(path), "Fixture save path exists");
        var error = 0;
        var warnings = 0;
        var ok = model.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref error, ref warnings);
        Require(ok && error == 0, "Saved " + Path.GetFileName(path) + " error=" + error);
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static void ExpectRejection(Action action, string description)
    {
        try { action(); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: " + description); return; }
        throw new InvalidOperationException("FAILED: " + description);
    }

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);

    private static ISldWorks? FindProcess(int pid)
    {
        GetRunningObjectTable(0, out var table);
        CreateBindCtx(0, out var context);
        table.EnumRunning(out var enumerator);
        var monikers = new IMoniker[1];
        try
        {
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                try
                {
                    monikers[0].GetDisplayName(context, null, out var name);
                    if (name.IndexOf("SolidWorks_PID_" + pid, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    table.GetObject(monikers[0], out var value);
                    if (value is ISldWorks app && app.GetProcessID() == pid) return app;
                }
                finally { Marshal.ReleaseComObject(monikers[0]); }
            }
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(context);
            Marshal.ReleaseComObject(table);
        }
    }
}
