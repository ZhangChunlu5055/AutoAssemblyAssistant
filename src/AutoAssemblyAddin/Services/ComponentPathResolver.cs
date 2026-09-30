using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace AutoAssemblyAddin.Services;

internal static class ComponentPathResolver
{
    public static string ResolveForExport(Component2 component, ModelDoc2 componentModel, string sourceAssemblyPath)
    {
        var searchDirectories = GetSearchDirectories(sourceAssemblyPath);
        var candidates = BuildCandidates(component, componentModel);

        if (TryResolve(candidates, searchDirectories, out var resolvedPath))
        {
            return resolvedPath;
        }

        throw new InvalidOperationException(
            $"无法解析子装配体 {component.Name2} 的真实文件路径。虚拟组件或未保存的装配体无法导出。");
    }

    public static bool TryResolveForRebuild(
        string storedPath,
        IEnumerable<string> searchDirectories,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        try
        {
            resolvedPath = ResolveForRebuild(storedPath, searchDirectories);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string ResolveForRebuild(string storedPath, IEnumerable<string> searchDirectories)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            throw new InvalidOperationException("JSON 中存在空的子装配体路径。");
        }

        if (File.Exists(storedPath) && !IsVirtualOrTempPath(storedPath))
        {
            return storedPath;
        }

        var candidates = new List<string> { storedPath };
        candidates.AddRange(ExtractFileNameCandidates(storedPath));

        if (TryResolve(candidates, searchDirectories, out var resolvedPath))
        {
            return resolvedPath;
        }

        throw new InvalidOperationException($"找不到子装配体文件：{storedPath}");
    }

    public static IEnumerable<string> GetSearchDirectories(string sourceAssemblyPath, string? jsonPath = null)
    {
        var directories = new List<string>();

        AddDirectoryIfExists(directories, Path.GetDirectoryName(sourceAssemblyPath));

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            AddDirectoryIfExists(directories, Path.GetDirectoryName(jsonPath));
        }

        return directories;
    }

    private static List<string> BuildCandidates(Component2 component, ModelDoc2 componentModel)
    {
        var candidates = new List<string>();

        var modelPath = componentModel.GetPathName();
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            candidates.Add(modelPath);
        }

        var componentPath = component.GetPathName();
        if (!string.IsNullOrWhiteSpace(componentPath))
        {
            candidates.Add(componentPath);
        }

        candidates.AddRange(candidates.ToList().SelectMany(ExtractFileNameCandidates));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ExtractFileNameCandidates(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            yield break;
        }

        yield return fileName;

        var caretIndex = fileName.LastIndexOf('^');
        if (caretIndex >= 0 && caretIndex < fileName.Length - 1)
        {
            yield return fileName.Substring(caretIndex + 1);
        }
    }

    private static bool TryResolve(
        IEnumerable<string> candidates,
        IEnumerable<string> searchDirectories,
        out string resolvedPath)
    {
        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate) && !IsVirtualOrTempPath(candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        foreach (var directory in searchDirectories.Where(Directory.Exists))
        {
            foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var fileName in ExtractFileNameCandidates(candidate))
                {
                    var directPath = Path.Combine(directory, fileName);
                    if (File.Exists(directPath))
                    {
                        resolvedPath = directPath;
                        return true;
                    }

                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    if (string.IsNullOrWhiteSpace(baseName))
                    {
                        continue;
                    }

                    var matches = Directory.GetFiles(directory, baseName + ".*", SearchOption.TopDirectoryOnly)
                        .Where(path => path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (matches.Length == 1)
                    {
                        resolvedPath = matches[0];
                        return true;
                    }
                }
            }
        }

        resolvedPath = string.Empty;
        return false;
    }

    private static bool IsVirtualOrTempPath(string path)
    {
        return path.IndexOf("VC~", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\Temp\swx", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"AppData\Local\Temp", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddDirectoryIfExists(ICollection<string> directories, string? directory)
    {
        if (directory == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (!directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            directories.Add(directory);
        }
    }
}
