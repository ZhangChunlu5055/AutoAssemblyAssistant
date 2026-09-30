using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoAssemblyAddin.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AutoAssemblyAddin.Services;

internal sealed class PoseService
{
    private readonly ISldWorks _app;

    public PoseService(ISldWorks app)
    {
        _app = app;
    }

    public string ExportCurrentAssembly()
    {
        var model = GetActiveAssembly("导出位姿");
        var assemblyPath = model.GetPathName();
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new InvalidOperationException("请先保存当前装配体，再执行导出。");
        }

        var components = CollectFirstLevelSubAssemblies(model);
        if (components.Count == 0)
        {
            throw new InvalidOperationException("未找到第一层子装配体。请确认当前装配体包含未隐藏、未抑制的子装配体。");
        }

        var document = new PoseExportDocument
        {
            SourceAssembly = assemblyPath,
            ExportedAt = DateTime.Now.ToString("O"),
            Components = components
        };

        var jsonPath = OutputPaths.CreateExportJsonPath(assemblyPath);
        PoseJson.Write(jsonPath, document);
        return jsonPath;
    }

    public RebuildResult RebuildFromJson(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !System.IO.File.Exists(jsonPath))
        {
            throw new InvalidOperationException("请选择有效的 JSON 位姿文件。");
        }

        var document = PoseJson.Read(jsonPath);
        var sourceAssemblyPath = document.SourceAssembly;
        if (string.IsNullOrWhiteSpace(sourceAssemblyPath))
        {
            sourceAssemblyPath = jsonPath;
        }

        var newModel = AssemblyDocumentFactory.CreateNewAssembly(_app);

        var newAssembly = (AssemblyDoc)newModel;
        var mathUtility = (MathUtility)_app.GetMathUtility();
        var insertedCount = 0;
        var skipped = new List<string>();
        var searchDirectories = ComponentPathResolver.GetSearchDirectories(sourceAssemblyPath, jsonPath);

        foreach (var pose in document.Components)
        {
            var displayName = string.IsNullOrWhiteSpace(pose.Name) ? pose.Path : pose.Name;

            if (!ComponentPathResolver.TryResolveForRebuild(pose.Path, searchDirectories, out var componentPath))
            {
                skipped.Add($"{displayName}（找不到文件）");
                continue;
            }

            if (pose.Transform == null || pose.Transform.Length != 16)
            {
                skipped.Add($"{displayName}（Transform 数据无效）");
                continue;
            }

            var component = (Component2)newAssembly.AddComponent5(
                componentPath,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                string.Empty,
                false,
                string.Empty,
                0,
                0,
                0);

            if (component == null)
            {
                skipped.Add($"{displayName}（插入失败：{componentPath}）");
                continue;
            }

            var transformData = (object)pose.Transform.Clone();
            var transform = (MathTransform)mathUtility.CreateTransform(transformData);
            component.Transform2 = transform;
            insertedCount++;
        }

        if (insertedCount == 0)
        {
            var detail = skipped.Count > 0
                ? $"跳过项：{string.Join("；", skipped)}"
                : "JSON 中没有可插入的子装配体。";
            throw new InvalidOperationException($"没有成功插入任何子装配体。{detail}");
        }

        newModel.EditRebuild3();
        newModel.ViewZoomtofit2();

        var savePath = OutputPaths.CreateRebuildAssemblyPath(sourceAssemblyPath, jsonPath);
        SaveDocument(newModel, savePath);

        return new RebuildResult
        {
            SavePath = savePath,
            InsertedCount = insertedCount,
            Skipped = skipped
        };
    }

    private List<ComponentPose> CollectFirstLevelSubAssemblies(ModelDoc2 assemblyModel)
    {
        var results = new List<ComponentPose>();
        var assembly = (AssemblyDoc)assemblyModel;
        var assemblyPath = assemblyModel.GetPathName();
        var components = (object[]?)assembly.GetComponents(false) ?? Array.Empty<object>();

        foreach (var item in components)
        {
            if (item is not Component2 component)
            {
                continue;
            }

            if (component.IsHidden(false))
            {
                continue;
            }

            if (component.GetSuppression() == (int)swComponentSuppressionState_e.swComponentSuppressed)
            {
                continue;
            }

            if (component.GetParent() != null)
            {
                continue;
            }

            var componentModel = (ModelDoc2?)component.GetModelDoc2();
            if (componentModel == null)
            {
                continue;
            }

            if (componentModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                continue;
            }

            var transform = (MathTransform?)component.Transform2;
            if (transform == null)
            {
                continue;
            }

            var transformData = (double[]?)transform.ArrayData;
            if (transformData == null || transformData.Length != 16)
            {
                continue;
            }

            string resolvedPath;
            try
            {
                resolvedPath = ComponentPathResolver.ResolveForExport(component, componentModel, assemblyPath);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            results.Add(new ComponentPose
            {
                Name = component.Name2,
                Path = resolvedPath,
                Transform = (double[])transformData.Clone()
            });
        }

        return results;
    }

    private ModelDoc2 GetActiveAssembly(string actionName)
    {
        var model = (ModelDoc2?)_app.ActiveDoc;
        if (model == null)
        {
            throw new InvalidOperationException($"请先打开一个装配体，再执行{actionName}。");
        }

        if (model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException($"当前文档不是装配体，无法{actionName}。");
        }

        return model;
    }

    private static void SaveDocument(ModelDoc2 model, string savePath)
    {
        var extension = (ModelDocExtension)model.Extension;
        var errors = 0;
        var warnings = 0;
        var saved = extension.SaveAs(
            savePath,
            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref errors,
            ref warnings);

        if (!saved || errors != 0)
        {
            throw new InvalidOperationException($"保存装配体失败：{savePath}，错误码 {errors}。");
        }
    }
}
