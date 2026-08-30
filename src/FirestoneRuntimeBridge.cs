using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

internal static class FirestoneRuntimeBridge
{
    private const string ExtensionId = "lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob";

    private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
    {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 256
    };

    public static FirestoneRuntimeExportResult Export(string outputRoot)
    {
        if(string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("An output directory is required.", "outputRoot");

        var dllPath = FindFirestoneBridgePath();
        var assembly = Assembly.LoadFrom(dllPath);
        var wrapperType = assembly.GetType("OverwolfUnitySpy.StaticMindVisionWrapper", true);
        var wrapper = Activator.CreateInstance(wrapperType, true);

        IEnumerable categories = null;
        try
        {
            categories = InvokeTaskResult(wrapperType, wrapper, "getAchievementCategories") as IEnumerable;
        }
        catch
        {
            // Per-achievement progress is still useful if category totals are unavailable.
        }

        var achievementsInfo = InvokeTaskResult(wrapperType, wrapper, "getAchievementsInfo");
        var achievements = GetPropertyValue(achievementsInfo, "Achievements") as IEnumerable;
        if(achievements == null)
            throw new InvalidDataException("Firestone returned no achievement collection.");

        var categoryRows = BuildCategoryRows(categories);
        var achievementRows = BuildAchievementRows(achievements);
        if(achievementRows.Count == 0)
            throw new InvalidDataException("Firestone returned an empty achievement collection.");

        Directory.CreateDirectory(outputRoot);
        var categoryPath = Path.Combine(outputRoot, "mindvision-official-categories.json");
        var achievementPath = Path.Combine(outputRoot, "mindvision-achievements.json");
        var summaryPath = Path.Combine(outputRoot, "mindvision-summary.json");
        var exportedAt = DateTime.Now;

        WriteJsonAtomically(categoryPath, categoryRows);
        WriteJsonAtomically(achievementPath, achievementRows);
        WriteJsonAtomically(summaryPath, new
        {
            ExportedAt = exportedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Source = "Firestone local runtime",
            Categories = categoryRows.Count,
            RuntimeAchievements = achievementRows.Count
        });

        return new FirestoneRuntimeExportResult
        {
            DllPath = dllPath,
            LatestOutputPath = achievementPath,
            ExportedAt = exportedAt,
            CategoryCount = categoryRows.Count,
            AchievementCount = achievementRows.Count
        };
    }

    internal static List<OfficialAchievementExportRow> BuildAchievementRows(IEnumerable values)
    {
        var rows = new List<OfficialAchievementExportRow>();
        if(values == null)
            return rows;

        foreach(var value in values)
        {
            var id = SafeInt(GetPropertyValue(value, "AchievementId"));
            if(id <= 0)
                continue;

            rows.Add(new OfficialAchievementExportRow
            {
                AchievementId = id,
                Progress = SafeInt(GetPropertyValue(value, "Progress")),
                Index = SafeInt(GetPropertyValue(value, "Index")),
                Status = SafeInt(GetPropertyValue(value, "Status"))
            });
        }

        return rows
            .GroupBy(row => row.AchievementId)
            .Select(group => group
                .OrderByDescending(row => row.Progress)
                .ThenByDescending(row => row.Status)
                .First())
            .OrderBy(row => row.AchievementId)
            .ToList();
    }

    internal static List<OfficialCategoryExportRow> BuildCategoryRows(IEnumerable values)
    {
        var rows = new List<OfficialCategoryExportRow>();
        if(values == null)
            return rows;

        foreach(var value in values)
        {
            var id = SafeInt(GetPropertyValue(value, "Id"));
            if(id <= 0)
                continue;

            var name = Convert.ToString(GetPropertyValue(value, "Name"), CultureInfo.InvariantCulture);
            var icon = Convert.ToString(GetPropertyValue(value, "Icon"), CultureInfo.InvariantCulture);
            var statsValue = GetPropertyValue(value, "Stats");
            var stats = new OfficialRuntimeCategoryNumbers
            {
                AvailablePoints = SafeInt(GetPropertyValue(statsValue, "AvailablePoints")),
                Points = SafeInt(GetPropertyValue(statsValue, "Points")),
                CompletedAchievements = SafeInt(GetPropertyValue(statsValue, "CompletedAchievements")),
                TotalAchievements = SafeInt(GetPropertyValue(statsValue, "TotalAchievements")),
                Unclaimed = SafeInt(GetPropertyValue(statsValue, "Unclaimed"))
            };

            rows.Add(new OfficialCategoryExportRow
            {
                Id = id,
                Name = name,
                Icon = icon,
                RuntimeStats = new OfficialRuntimeCategoryStats
                {
                    Id = id,
                    Name = name,
                    Icon = icon,
                    Stats = stats
                },
                AchievementCount = stats.TotalAchievements,
                Achievements = new List<OfficialAchievementExportRow>()
            });
        }

        return rows
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .OrderBy(row => row.Id)
            .ToList();
    }

    private static string FindFirestoneBridgePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Overwolf",
            "Extensions",
            ExtensionId);
        if(!Directory.Exists(root))
            throw new DirectoryNotFoundException("Firestone extension directory was not found: " + root);

        var dll = Directory.GetDirectories(root)
            .Select(path => new DirectoryInfo(path))
            .Select(directory => new
            {
                Directory = directory,
                DllPath = Path.Combine(directory.FullName, "plugins", "OverwolfUnitySpy.dll")
            })
            .Where(item => File.Exists(item.DllPath))
            .OrderByDescending(item => item.Directory.LastWriteTimeUtc)
            .ThenByDescending(item => item.Directory.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.DllPath)
            .FirstOrDefault();
        if(string.IsNullOrWhiteSpace(dll))
            throw new FileNotFoundException("OverwolfUnitySpy.dll was not found under the Firestone extension directory.");
        return dll;
    }

    private static object InvokeTaskResult(Type wrapperType, object wrapper, string methodName)
    {
        var method = wrapperType.GetMethod(methodName);
        if(method == null)
            throw new MissingMethodException(wrapperType.FullName, methodName);

        object task;
        try
        {
            task = method.Invoke(wrapper, new object[] { null });
        }
        catch(TargetInvocationException ex)
        {
            throw new InvalidOperationException(methodName + " could not be started.", ex.InnerException ?? ex);
        }

        var waitMethod = task.GetType().GetMethod("Wait", new[] { typeof(int) });
        if(waitMethod == null)
            throw new InvalidOperationException("Task.Wait(int) was not found for " + methodName + ".");
        if(!(bool)waitMethod.Invoke(task, new object[] { 15000 }))
            throw new TimeoutException(methodName + " timed out.");

        var isFaulted = task.GetType().GetProperty("IsFaulted");
        if(isFaulted != null && (bool)isFaulted.GetValue(task, null))
        {
            var exception = task.GetType().GetProperty("Exception");
            var taskException = exception == null ? null : exception.GetValue(task, null) as Exception;
            throw new InvalidOperationException(methodName + " failed.", taskException);
        }

        var result = task.GetType().GetProperty("Result");
        return result == null ? null : result.GetValue(task, null);
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if(instance == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        var dictionary = instance as IDictionary;
        if(dictionary != null)
        {
            foreach(DictionaryEntry entry in dictionary)
            {
                if(string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), propertyName, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
            return null;
        }

        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property == null ? null : property.GetValue(instance, null);
    }

    private static int SafeInt(object value)
    {
        if(value == null)
            return 0;
        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }
    }

    private static void WriteJsonAtomically(string path, object value)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, Serializer.Serialize(value), new UTF8Encoding(false));
        try
        {
            if(File.Exists(path))
                File.Replace(tempPath, path, null, true);
            else
                File.Move(tempPath, path);
        }
        catch
        {
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);
        }
    }
}

internal sealed class FirestoneRuntimeExportResult
{
    public string DllPath { get; set; }

    public string LatestOutputPath { get; set; }

    public DateTime ExportedAt { get; set; }

    public int CategoryCount { get; set; }

    public int AchievementCount { get; set; }
}
