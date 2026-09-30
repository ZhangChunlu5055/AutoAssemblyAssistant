using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using AutoAssemblyAddin.Models;

namespace AutoAssemblyAddin.Services;

internal static class PoseJson
{
    private static readonly JavaScriptSerializer Serializer = CreateSerializer();

    public static void Write(string path, PoseExportDocument document)
    {
        var json = Serializer.Serialize(document);
        File.WriteAllText(path, FormatJson(json), Encoding.UTF8);
    }

    public static PoseExportDocument Read(string path)
    {
        var json = File.ReadAllText(path);
        var document = Serializer.Deserialize<PoseExportDocument>(json);
        if (document == null)
        {
            throw new InvalidOperationException("JSON 文件格式无效。");
        }

        if (document.Components == null || document.Components.Count == 0)
        {
            throw new InvalidOperationException("JSON 中没有可重建的子装配体数据。");
        }

        return document;
    }

    private static JavaScriptSerializer CreateSerializer()
    {
        var serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 32
        };
        return serializer;
    }

    private static string FormatJson(string json)
    {
        var builder = new StringBuilder(json.Length + 128);
        var indent = 0;
        var inString = false;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];

            if (ch == '"')
            {
                var escaped = i > 0 && json[i - 1] == '\\';
                if (!escaped)
                {
                    inString = !inString;
                }

                builder.Append(ch);
                continue;
            }

            if (inString)
            {
                builder.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '{':
                case '[':
                    builder.Append(ch);
                    builder.AppendLine();
                    indent++;
                    builder.Append(new string(' ', indent * 2));
                    break;
                case '}':
                case ']':
                    builder.AppendLine();
                    indent--;
                    builder.Append(new string(' ', indent * 2));
                    builder.Append(ch);
                    break;
                case ',':
                    builder.Append(ch);
                    builder.AppendLine();
                    builder.Append(new string(' ', indent * 2));
                    break;
                case ':':
                    builder.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(ch))
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
