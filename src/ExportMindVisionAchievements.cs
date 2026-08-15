using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Web.Script.Serialization;

internal static class ExportMindVisionAchievementsProgram
{
    private const string DefaultExtensionId = "lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob";
    private const string AchievementsBaseUrl = "https://static.zerotoheroes.com/hearthstone/data/achievements";
    private const string AchievementConfigurationBaseUrl = "https://static.zerotoheroes.com/hearthstone/data/achievements/configuration";

    private static readonly string[] AchievementFiles =
    {
        "hearthstone_game_zhCN",
        "global",
        "battlegrounds2",
        "dungeon_run",
        "monster_hunt",
        "rumble_run",
        "dalaran_heist",
        "tombs_of_terror",
        "amazing_plays",
        "competitive_ladder",
        "deckbuilding",
        "galakrond",
        "thijs"
    };

    private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
    {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
    };

    public static int Main(string[] args)
    {
        try
        {
            var outputRoot = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json", "mindvision-export");
            Directory.CreateDirectory(outputRoot);

            var extensionRoot = GetExtensionRoot();
            var dllPath = Path.Combine(extensionRoot, "plugins", "OverwolfUnitySpy.dll");
            if(!File.Exists(dllPath))
                throw new FileNotFoundException("OverwolfUnitySpy.dll not found.", dllPath);

            var assembly = Assembly.LoadFrom(dllPath);
            var wrapperType = assembly.GetType("OverwolfUnitySpy.StaticMindVisionWrapper", true);
            var wrapper = Activator.CreateInstance(wrapperType, true);

            var categories = InvokeTaskResult(wrapperType, wrapper, "getAchievementCategories") as IEnumerable;
            var achievementsInfo = InvokeTaskResult(wrapperType, wrapper, "getAchievementsInfo");
            var runtimeAchievements = GetPropertyValue(achievementsInfo, "Achievements") as IEnumerable;

            var referenceAchievements = LoadReferenceAchievements();
            var referenceByHsId = referenceAchievements
                .Where(item => item.HsAchievementId > 0)
                .GroupBy(item => item.HsAchievementId)
                .ToDictionary(group => group.Key, group => group.First());
            var categoryConfiguration = LoadCategoryConfiguration();
            var typeToRootCategory = BuildTypeToRootCategoryMap(categoryConfiguration);

            var categoryObjects = ToList(categories)
                .Select(item => new
                {
                    Raw = SnapshotObject(item, 6),
                    Flat = FlattenCategory(item)
                })
                .ToList();

            var runtimeCategoryStats = categoryObjects
                .Select(item => item.Raw as Dictionary<string, object>)
                .Where(item => item != null)
                .GroupBy(item => SafeInt(item.ContainsKey("Id") ? item["Id"] : null))
                .ToDictionary(g => g.Key, g => g.First());

            var runtimeAchievementObjects = ToList(runtimeAchievements)
                .Select(item =>
                {
                    var achievementId = SafeInt(GetPropertyValue(item, "AchievementId"));
                    ReferenceAchievement reference;
                    referenceByHsId.TryGetValue(achievementId, out reference);
                    return new
                    {
                        AchievementId = achievementId,
                        Progress = SafeInt(GetPropertyValue(item, "Progress")),
                        Index = SafeInt(GetPropertyValue(item, "Index")),
                        Status = SafeInt(GetPropertyValue(item, "Status")),
                        Raw = SnapshotObject(item, 3),
                        Reference = reference,
                        RootCategory = reference != null && !string.IsNullOrWhiteSpace(reference.Type) && typeToRootCategory.ContainsKey(reference.Type)
                            ? typeToRootCategory[reference.Type]
                            : null
                    };
                })
                .ToList();

            var officialCategories = typeToRootCategory.Values
                .GroupBy(item => item.Id)
                .Select(group => group.First())
                .OrderBy(item => item.Id)
                .Select(category =>
                {
                    Dictionary<string, object> stats;
                    runtimeCategoryStats.TryGetValue(category.Id, out stats);
                    var achievementsForCategory = runtimeAchievementObjects
                        .Where(item => item.RootCategory != null && item.RootCategory.Id == category.Id)
                        .OrderByDescending(item => item.Status == 4 || item.Status == 2)
                        .ThenByDescending(item => item.Reference != null ? item.Reference.Root : false)
                        .ThenBy(item => item.Reference != null ? item.Reference.HsSectionId : int.MaxValue)
                        .ThenBy(item => item.Reference != null ? item.Reference.Priority : int.MaxValue)
                        .ThenBy(item => item.AchievementId)
                        .ToList();

                    return new
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Icon = category.Icon,
                        RuntimeStats = stats,
                        AchievementCount = achievementsForCategory.Count,
                        Achievements = achievementsForCategory
                    };
                })
                .ToList();

            var categoriesPath = Path.Combine(outputRoot, "mindvision-achievement-categories.json");
            var achievementsPath = Path.Combine(outputRoot, "mindvision-achievements.json");
            var referencePath = Path.Combine(outputRoot, "mindvision-achievement-reference.json");
            var configPath = Path.Combine(outputRoot, "mindvision-achievement-category-config.json");
            var officialCategoriesPath = Path.Combine(outputRoot, "mindvision-official-categories.json");
            var summaryPath = Path.Combine(outputRoot, "mindvision-summary.json");

            File.WriteAllText(categoriesPath, Serializer.Serialize(categoryObjects));
            File.WriteAllText(achievementsPath, Serializer.Serialize(runtimeAchievementObjects));
            File.WriteAllText(referencePath, Serializer.Serialize(referenceAchievements));
            File.WriteAllText(configPath, Serializer.Serialize(categoryConfiguration));
            File.WriteAllText(officialCategoriesPath, Serializer.Serialize(officialCategories));
            File.WriteAllText(
                summaryPath,
                Serializer.Serialize(new
                {
                    ExportedAt = DateTimeOffset.Now,
                    DllPath = dllPath,
                    Categories = categoryObjects.Count,
                    RuntimeAchievements = runtimeAchievementObjects.Count,
                    ReferenceAchievements = referenceAchievements.Count,
                    OfficialCategories = officialCategories.Count
                }));

            var historyTimestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmm");
            var historyDir = Path.Combine(outputRoot, "history", historyTimestamp);
            Directory.CreateDirectory(historyDir);
            foreach(var src in new[] { categoriesPath, achievementsPath, referencePath, configPath, officialCategoriesPath, summaryPath })
                File.Copy(src, Path.Combine(historyDir, Path.GetFileName(src)), overwrite: true);

            Console.WriteLine("Export complete");
            Console.WriteLine(categoriesPath);
            Console.WriteLine(achievementsPath);
            Console.WriteLine(referencePath);
            Console.WriteLine(configPath);
            Console.WriteLine(officialCategoriesPath);
            Console.WriteLine(summaryPath);
            Console.WriteLine("History: " + historyDir);
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string GetExtensionRoot()
    {
        var extensionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Overwolf",
            "Extensions",
            DefaultExtensionId);
        var latestVersion = Directory.GetDirectories(extensionsDir)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if(latestVersion == null)
            throw new DirectoryNotFoundException("Firestone extension directory not found: " + extensionsDir);
        return latestVersion;
    }

    private static object InvokeTaskResult(Type wrapperType, object wrapper, string methodName)
    {
        var method = wrapperType.GetMethod(methodName);
        if(method == null)
            throw new MissingMethodException(wrapperType.FullName, methodName);

        var task = method.Invoke(wrapper, new object[] { null });
        var waitMethod = task.GetType().GetMethod("Wait", new[] { typeof(int) });
        if(waitMethod == null)
            throw new InvalidOperationException("Task.Wait(int) not found for " + methodName);

        var completed = (bool)waitMethod.Invoke(task, new object[] { 15000 });
        if(!completed)
            throw new TimeoutException(methodName + " timed out.");

        var isFaultedProp = task.GetType().GetProperty("IsFaulted");
        if(isFaultedProp != null && (bool)isFaultedProp.GetValue(task, null))
        {
            var exceptionProp = task.GetType().GetProperty("Exception");
            var aggEx = exceptionProp?.GetValue(task, null) as Exception;
            throw new InvalidOperationException(methodName + " faulted.", aggEx);
        }

        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static List<object> ToList(IEnumerable values)
    {
        var result = new List<object>();
        if(values == null)
            return result;

        foreach(var item in values)
            result.Add(item);
        return result;
    }

    private static object SnapshotObject(object value, int depth)
    {
        if(value == null)
            return null;
        if(depth <= 0)
            return value.ToString();

        var type = value.GetType();
        if(type.IsPrimitive || value is string || value is decimal || value is DateTime || value is DateTimeOffset)
            return value;

        var enumerable = value as IEnumerable;
        if(enumerable != null && !(value is IDictionary))
        {
            var list = new List<object>();
            foreach(var item in enumerable)
                list.Add(SnapshotObject(item, depth - 1));
            return list;
        }

        var dictionary = value as IDictionary;
        if(dictionary != null)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach(DictionaryEntry entry in dictionary)
                map[Convert.ToString(entry.Key)] = SnapshotObject(entry.Value, depth - 1);
            return map;
        }

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach(var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if(property.GetIndexParameters().Length > 0)
                continue;

            object propertyValue;
            try
            {
                propertyValue = property.GetValue(value, null);
            }
            catch
            {
                continue;
            }

            result[property.Name] = SnapshotObject(propertyValue, depth - 1);
        }
        return result;
    }

    private static Dictionary<string, object> FlattenCategory(object value)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if(value == null)
            return map;

        foreach(var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if(property.GetIndexParameters().Length > 0)
                continue;

            object propertyValue;
            try
            {
                propertyValue = property.GetValue(value, null);
            }
            catch
            {
                continue;
            }

            if(propertyValue == null)
            {
                map[property.Name] = null;
                continue;
            }

            if(propertyValue is string || propertyValue.GetType().IsPrimitive || propertyValue is decimal)
            {
                map[property.Name] = propertyValue;
                continue;
            }

            var propertyEnumerable = propertyValue as IEnumerable;
            if(propertyEnumerable != null && !(propertyValue is string))
            {
                map[property.Name + "Count"] = ToList(propertyEnumerable).Count;
                continue;
            }

            map[property.Name] = propertyValue.ToString();
        }

        return map;
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if(instance == null)
            return null;

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property != null ? property.GetValue(instance, null) : null;
    }

    private static int SafeInt(object value)
    {
        if(value == null)
            return 0;
        if(value is int)
            return (int)value;

        int parsed;
        return int.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
    }

    private static List<ReferenceAchievement> LoadReferenceAchievements()
    {
        var all = new List<ReferenceAchievement>();
        using(var client = new WebClient())
        {
            client.Encoding = System.Text.Encoding.UTF8;
            foreach(var file in AchievementFiles)
            {
                var url = AchievementsBaseUrl + "/" + file + ".json";
                var raw = client.DownloadString(url);
                var parsed = Serializer.DeserializeObject(raw) as object[];
                if(parsed == null)
                    continue;

                foreach(var item in parsed.OfType<Dictionary<string, object>>())
                    all.Add(ToReferenceAchievement(item));
            }
        }
        return all;
    }

    private static Dictionary<string, object> LoadCategoryConfiguration()
    {
        using(var client = new WebClient())
        {
            client.Encoding = System.Text.Encoding.UTF8;
            var raw = client.DownloadString(AchievementConfigurationBaseUrl + "/hearthstone_game_zhCN.json");
            return Serializer.DeserializeObject(raw) as Dictionary<string, object>;
        }
    }

    private static Dictionary<string, RootCategoryInfo> BuildTypeToRootCategoryMap(Dictionary<string, object> configuration)
    {
        var map = new Dictionary<string, RootCategoryInfo>(StringComparer.OrdinalIgnoreCase);
        if(configuration == null || !configuration.ContainsKey("categories"))
            return map;

        var topCategories = configuration["categories"] as object[];
        if(topCategories == null)
            return map;

        foreach(var item in topCategories.OfType<Dictionary<string, object>>())
        {
            var rootInfo = new RootCategoryInfo
            {
                Id = ParseTrailingInt(GetString(item, "id")),
                Key = GetString(item, "id"),
                Name = GetString(item, "name"),
                Icon = GetString(item, "icon")
            };

            foreach(var achievementType in CollectAchievementTypes(item))
            {
                if(!map.ContainsKey(achievementType))
                    map[achievementType] = rootInfo;
            }
        }
        return map;
    }

    private static IEnumerable<string> CollectAchievementTypes(Dictionary<string, object> category)
    {
        var result = new List<string>();
        if(category == null)
            return result;

        var direct = category.ContainsKey("achievementTypes") ? category["achievementTypes"] as object[] : null;
        if(direct != null)
            result.AddRange(direct.Select(Convert.ToString).Where(value => !string.IsNullOrWhiteSpace(value)));

        var children = category.ContainsKey("categories") ? category["categories"] as object[] : null;
        if(children != null)
        {
            foreach(var child in children.OfType<Dictionary<string, object>>())
                result.AddRange(CollectAchievementTypes(child));
        }

        return result;
    }

    private static int ParseTrailingInt(string value)
    {
        if(string.IsNullOrWhiteSpace(value))
            return 0;

        var lastUnderscore = value.LastIndexOf('_');
        if(lastUnderscore < 0 || lastUnderscore >= value.Length - 1)
            return 0;

        int parsed;
        return int.TryParse(value.Substring(lastUnderscore + 1), out parsed) ? parsed : 0;
    }

    private static ReferenceAchievement ToReferenceAchievement(Dictionary<string, object> item)
    {
        return new ReferenceAchievement
        {
            Id = GetString(item, "id"),
            HsAchievementId = GetInt(item, "hsAchievementId"),
            HsSectionId = GetInt(item, "hsSectionId"),
            HsRewardTrackXp = GetInt(item, "hsRewardTrackXp"),
            Name = GetString(item, "name"),
            DisplayName = GetString(item, "displayName"),
            Text = GetString(item, "text"),
            CompletedText = GetString(item, "completedText"),
            EmptyText = GetString(item, "emptyText"),
            Type = GetString(item, "type"),
            Icon = GetString(item, "icon"),
            DisplayCardId = GetString(item, "displayCardId"),
            DisplayCardType = GetString(item, "displayCardType"),
            Points = GetInt(item, "points"),
            Priority = GetInt(item, "priority"),
            Quota = GetInt(item, "quota"),
            Root = GetBool(item, "root")
        };
    }

    private static string GetString(IDictionary<string, object> item, string key)
    {
        object value;
        return item != null && item.TryGetValue(key, out value) && value != null
            ? Convert.ToString(value)
            : null;
    }

    private static int GetInt(IDictionary<string, object> item, string key)
    {
        object value;
        if(item == null || !item.TryGetValue(key, out value) || value == null)
            return 0;

        if(value is int)
            return (int)value;
        if(value is long)
            return (int)(long)value;
        if(value is decimal)
            return (int)Math.Round((decimal)value);
        if(value is double)
            return (int)Math.Round((double)value);
        if(value is float)
            return (int)Math.Round((float)value);

        decimal parsed;
        return decimal.TryParse(Convert.ToString(value), out parsed)
            ? (int)Math.Round(parsed)
            : 0;
    }

    private static bool GetBool(IDictionary<string, object> item, string key)
    {
        object value;
        if(item == null || !item.TryGetValue(key, out value) || value == null)
            return false;

        if(value is bool)
            return (bool)value;

        bool parsed;
        return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
    }

    private sealed class ReferenceAchievement
    {
        public string Id { get; set; }
        public int HsAchievementId { get; set; }
        public int HsSectionId { get; set; }
        public int HsRewardTrackXp { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Text { get; set; }
        public string CompletedText { get; set; }
        public string EmptyText { get; set; }
        public string Type { get; set; }
        public string Icon { get; set; }
        public string DisplayCardId { get; set; }
        public string DisplayCardType { get; set; }
        public int Points { get; set; }
        public int Priority { get; set; }
        public int Quota { get; set; }
        public bool Root { get; set; }
    }

    private sealed class RootCategoryInfo
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
    }
}
