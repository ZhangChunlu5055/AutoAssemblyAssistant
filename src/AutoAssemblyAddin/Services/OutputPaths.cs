using System;
using System.IO;

namespace AutoAssemblyAddin.Services;

internal static class OutputPaths
{
    public const string OutputFolderName = "AutoAssembly";

    public static string GetOutputDirectory(string assemblyPath)
    {
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException("无法确定装配体所在目录。");

        var outputDirectory = Path.Combine(assemblyDirectory, OutputFolderName);
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    public static string CreateExportJsonPath(string assemblyPath)
    {
        var outputDirectory = GetOutputDirectory(assemblyPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        return Path.Combine(outputDirectory, $"{assemblyName}_poses_{timestamp}.json");
    }

    public static string CreateRebuildAssemblyPath(string sourceAssemblyPath, string? jsonPath = null)
    {
        string baseDirectory;
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            baseDirectory = Path.GetDirectoryName(jsonPath)
                ?? GetOutputDirectory(sourceAssemblyPath);
        }
        else
        {
            baseDirectory = GetOutputDirectory(sourceAssemblyPath);
        }

        Directory.CreateDirectory(baseDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var assemblyName = Path.GetFileNameWithoutExtension(sourceAssemblyPath);
        return Path.Combine(baseDirectory, $"{assemblyName}_rebuilt_{timestamp}.SLDASM");
    }
}
