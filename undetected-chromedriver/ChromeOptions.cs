using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenQA.Selenium.Chromium;

public class ChromeOptions : OpenQA.Selenium.Chrome.ChromeOptions
{
    private string _userDataDir;

    public string UserDataDir
    {
        get => _userDataDir;
        set
        {
            var path = Path.GetFullPath(value);
            _userDataDir = Path.GetFullPath(path);
        }
    }

    private static Dictionary<string, object> UndotKey(string key, object value)
    {
        if (key.Contains("."))
        {
            var parts = key.Split('.', 2);
            key = parts[0];
            value = UndotKey(parts[1], value);
        }
        return new Dictionary<string, object> { { key, value } };
    }

    private static void MergeNested(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        foreach (var key in b.Keys)
        {
            if (a.ContainsKey(key))
            {
                if (a[key] is Dictionary<string, object> aDict && b[key] is Dictionary<string, object> bDict)
                {
                    MergeNested(aDict, bDict);
                    continue;
                }
            }
            a[key] = b[key];
        }
    }

    public void HandlePrefs(string userDataDir)
    {
        var cap = this.ToCapabilities();
        var dic = (Dictionary<string, object>)cap.GetCapability(this.CapabilityName);
        var prefs = dic["prefs"] as Dictionary<string, object>;

        userDataDir ??= _userDataDir;
        var defaultPath = Path.Combine(userDataDir, "Default");
        Directory.CreateDirectory(defaultPath);

        var undotPrefs = new Dictionary<string, object>();
        foreach (var kvp in (Dictionary<string, object>)prefs)
        {
            var undotted = UndotKey(kvp.Key, kvp.Value);
            MergeNested(undotPrefs, undotted);
        }

        var prefsFile = Path.Combine(defaultPath, "Preferences");
        if (File.Exists(prefsFile))
        {
            using (var reader = new StreamReader(prefsFile))
            {
                var existingPrefs = JsonConvert.DeserializeObject<Dictionary<string, object>>(reader.ReadToEnd());
                MergeNested(existingPrefs, undotPrefs);
                undotPrefs = existingPrefs; // Overwrite with merged prefs
            }
        }

        using (var writer = new StreamWriter(prefsFile, false))
        {
            writer.Write(JsonConvert.SerializeObject(undotPrefs, Formatting.Indented));
        }
        prefs.Clear();
        //experimentalOptions.Remove("prefs");

    }

    public static ChromeOptions FromOptions(ChromeOptions options)
    {
        var newOptions = new ChromeOptions();
        foreach (var kvp in options.GetType().GetProperties())
        {
            newOptions.GetType().GetProperty(kvp.Name)?.SetValue(newOptions, kvp.GetValue(options));
        }
        return newOptions;
    }
}