using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AutoAssemblyAddin.Services;

internal static class AssemblyDocumentFactory
{
    public static ModelDoc2 CreateNewAssembly(ISldWorks app)
    {
        var templatePath = ResolveAssemblyTemplate(app);
        var swApp = (SldWorks)app;

        var model = (ModelDoc2?)swApp.NewDocument(templatePath, 0, 0, 0);
        if (model == null)
        {
            throw new InvalidOperationException($"无法创建新装配体。模板：{templatePath}");
        }

        if (model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException($"模板不是装配体模板：{templatePath}");
        }

        return model;
    }

    private static string ResolveAssemblyTemplate(ISldWorks app)
    {
        var candidates = new List<string>();

        var preferred = app.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            candidates.Add(NormalizeTemplatePath(preferred));
        }

        candidates.AddRange(GetDefaultTemplatePaths());

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "未找到可用的装配体模板。请在 SolidWorks 系统选项 > 默认模板 中设置装配体模板。");
    }

    private static IEnumerable<string> GetDefaultTemplatePaths()
    {
        yield return @"C:\ProgramData\SolidWorks\SOLIDWORKS 2024\templates\gb_assembly.asmdot";
        yield return @"C:\ProgramData\SolidWorks\SOLIDWORKS 2024\templates\Assembly.asmdot";

        var programDataRoot = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
            "SolidWorks");
        if (!Directory.Exists(programDataRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(programDataRoot, "*.asmdot", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.IndexOf("assembly", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return file;
            }
        }
    }

    private static string NormalizeTemplatePath(string templatePath)
    {
        return templatePath.Trim().Trim('"');
    }
}
