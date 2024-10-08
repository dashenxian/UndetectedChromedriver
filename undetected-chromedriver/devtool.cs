using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

public class Structure : Dictionary<string, object>
{
    public Structure(IDictionary<string, object> dictionary = null)
    {
        if (dictionary != null)
        {
            foreach (var kvp in dictionary)
            {
                if (kvp.Value is IDictionary<string, object> dict)
                {
                    this[kvp.Key] = new Structure(dict);
                }
                else if (kvp.Value is IEnumerable<object> list && !(kvp.Value is string))
                {
                    this[kvp.Key] = list.Select(i => new Structure(i as IDictionary<string, object>)).ToList();
                }
                else
                {
                    this[kvp.Key] = kvp.Value;
                }
            }
        }
        base["__dict__"] = this;
    }

    public object this[string key]
    {
        get => base[key];
        set => base[key] = value;
    }

    public void NormalizeStrings()
    {
        var keys = this.Keys.ToList();
        foreach (var key in keys)
        {
            if (this[key] is string str)
            {
                this[key] = str.Trim();
            }
        }
    }
}
public static class Timeout
{
    public static Func<Func<Task>, Task> Create(int seconds, Action<Func<Task>> onTimeout = null)
    {
        return async func =>
        {
            using var cts = new CancellationTokenSource();
            var task = Task.Run(func);
            var delayTask = Task.Delay(seconds * 1000, cts.Token);
            var completedTask = await Task.WhenAny(task, delayTask);

            if (completedTask == delayTask)
            {
                onTimeout?.Invoke(func);
                throw new TimeoutException("Function call timed out");
            }
            else
            {
                cts.Cancel(); // Cancel the delay
                await task; // Return the result of the original task
            }
        };
    }
}
public class Test
{
    private static readonly ILogger Logger = null;

    public static void Collector(Chrome driver, ManualResetEvent stopEvent, Func<List<string>, Task> onEventCoro = null, string[] listenEvents = null)
    {
        listenEvents ??= new[] { "browser", "network", "performance" };

        //Task.Run(async () =>
        //{
        //    while (!stopEvent.WaitOne(0))
        //    {
        //        var logLines = new List<string>();
        //        foreach (var type in listenEvents)
        //        {
        //            try
        //            {
        //                logLines.AddRange(await driver.GetLogAsync(type));
        //            }
        //            catch (Exception ex)
        //            {
        //                Logger.LogError(ex, "");
        //            }
        //        }

        //        if (logLines.Count > 0 && onEventCoro != null)
        //        {
        //            await onEventCoro(logLines);
        //        }

        //        await Task.Delay(100); // Adjust delay as needed
        //    }
        //});
    }

    public static async Task OnEvent(List<string> data)
    {
        Console.WriteLine("on_event");
        Console.WriteLine("data: " + string.Join(", ", data));
    }

    //public static void Main()
    //{
    //    var options = new ChromeOptions();
    //    options.ToCapabilities()["goog:loggingPrefs"]= new Dictionary<string, object>
    //    {
    //        { "performance", "ALL" },
    //        { "browser", "ALL" },
    //        { "network", "ALL" }
    //    };

    //    using var driver = new Chrome(options: options);
    //    var collectorStop = new ManualResetEvent(false);
    //    Collector(driver, collectorStop, OnEvent);

    //    driver.Navigate().GoToUrl("https://nowsecure.nl");
    //    Thread.Sleep(10000); // Wait for 10 seconds

    //    collectorStop.Set(); // Stop the collector
    //    driver.Quit();
    //}
}