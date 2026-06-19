using System.Text;
using System.Text.Json;
using CopyFinder.Models;

namespace CopyFinder.Services;

public static class DuplicateReportFormatter
{
    public static string BuildJsonReport(IEnumerable<DuplicateReportFile> files)
    {
        var rows = files.Select(file => new
        {
            file.GroupId,
            file.Role,
            file.IsSelected,
            file.Size,
            file.Hash,
            file.ImageWidth,
            file.ImageHeight,
            file.LastWriteTime,
            file.Path,
            IsNetworkPath = FilePathClassifier.IsNetworkPath(file.Path),
            DeleteStatus = GetDeleteStatus(file)
        });

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildCsvReport(IEnumerable<DuplicateReportFile> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GroupId,Role,Selected,Size,Hash,ImageWidth,ImageHeight,Modified,NetworkPath,DeleteStatus,Path");

        foreach (var file in files)
        {
            builder.Append(file.GroupId).Append(',')
                .Append(Csv(file.Role)).Append(',')
                .Append(file.IsSelected).Append(',')
                .Append(file.Size).Append(',')
                .Append(Csv(file.Hash)).Append(',')
                .Append(file.ImageWidth?.ToString() ?? string.Empty).Append(',')
                .Append(file.ImageHeight?.ToString() ?? string.Empty).Append(',')
                .Append(Csv(file.LastWriteTime.ToString("O"))).Append(',')
                .Append(FilePathClassifier.IsNetworkPath(file.Path)).Append(',')
                .Append(Csv(GetDeleteStatus(file))).Append(',')
                .Append(Csv(file.Path))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string GetDeleteStatus(DuplicateReportFile file)
    {
        return file.IsOriginal ? "Kept" : file.IsSelected ? "Selected" : "Not selected";
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
