using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoAssemblyAddin;

internal static class ComRegistration
{
    public const string ProgId = "AutoAssemblyAddin.SwAddin";
    public const string AddinGuid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890";
    public const string Title = "Auto Assembly Pose";
    public const string Description = "Export and rebuild first-level sub-assembly poses";

    [ComRegisterFunction]
    public static void RegisterFunction(Type type)
    {
        RegisterAddin(type);
    }

    [ComUnregisterFunction]
    public static void UnregisterFunction(Type type)
    {
        UnregisterAddin();
    }

    private static void RegisterAddin(Type type)
    {
        var assemblyPath = type.Assembly.Location;
        var keyPath = $@"SOFTWARE\SolidWorks\AddIns\{{{AddinGuid}}}";

        using (var key = Registry.LocalMachine.CreateSubKey(keyPath))
        {
            key?.SetValue(null, 0);
            key?.SetValue("Description", Description);
            key?.SetValue("Title", Title);
        }

        using (var startupKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\SolidWorks\AddInsStartup\{{{AddinGuid}}}"))
        {
            startupKey?.SetValue(null, 1, RegistryValueKind.DWord);
        }

        using (var progIdKey = Registry.ClassesRoot.CreateSubKey($@"{ProgId}\CLSID"))
        {
            progIdKey?.SetValue(null, $"{{{AddinGuid}}}");
        }

        using (var clsidKey = Registry.ClassesRoot.CreateSubKey($@"CLSID\{{{AddinGuid}}}"))
        {
            if (clsidKey == null) throw new InvalidOperationException("Cannot create COM registration key.");
            clsidKey.SetValue(null, Title);
            using var inproc = clsidKey.CreateSubKey("InprocServer32");
            inproc?.SetValue(null, assemblyPath);
            inproc?.SetValue("ThreadingModel", "Both");
            inproc?.SetValue("Class", type.FullName);
            inproc?.SetValue("Assembly", type.Assembly.FullName);
            inproc?.SetValue("RuntimeVersion", type.Assembly.ImageRuntimeVersion);
            inproc?.SetValue("CodeBase", assemblyPath);
        }
    }

    private static void UnregisterAddin()
    {
        Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\AddIns\{{{AddinGuid}}}", false);
        Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\AddInsStartup\{{{AddinGuid}}}", false);
        Registry.ClassesRoot.DeleteSubKeyTree(ProgId, false);
        Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{{{AddinGuid}}}", false);
    }
}
