using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
//using Serilog; // 用于记录日志的库
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

public class Reactor : IDisposable
{
    private static readonly ILogger<Reactor> Log = new LoggerFactory().CreateLogger<Reactor>();
    private readonly ChromeDriver _driver;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConcurrentDictionary<string, Action<Dictionary<string, object>>> _handlers;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public Reactor(ChromeDriver driver)
    {
        _driver = driver;
        _cancellationTokenSource = new CancellationTokenSource();
        _handlers = new ConcurrentDictionary<string, Action<Dictionary<string, object>>>();
    }

    public void AddEventHandler(string methodName, Action<Dictionary<string, object>> callback)
    {
        _handlers[methodName.ToLower()] = callback;
    }

    public bool Running => !_cancellationTokenSource.IsCancellationRequested;

    public async Task StartAsync()
    {
        try
        {
            await Listen();
        }
        catch (Exception e)
        {
            Log.LogWarning("Reactor.StartAsync() => {0}", e.Message);
        }
    }

    private async Task Listen()
    {
        while (Running)
        {
            await WaitServiceStarted();
            await Task.Delay(1000); // 等待1秒

            try
            {
                await _lock.WaitAsync(); // 异步锁定

                var logEntries = _driver.Manage().Logs.GetLog("performance");
                foreach (var entry in logEntries)
                {
                    try
                    {
                        var objSerialized = entry.Message;
                        var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(objSerialized);
                        var message = obj.GetValueOrDefault("message") as Dictionary<string, object>;
                        var method = message?.GetValueOrDefault("method")?.ToString();

                        if (method != null)
                        {
                            if (_handlers.ContainsKey("*"))
                            {
                                _handlers["*"](message);
                            }
                            else if (_handlers.ContainsKey(method.ToLower()))
                            {
                                _handlers[method.ToLower()](message);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.LogError(e, "Error processing log entry");
                    }
                }
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("invalid session id"))
                {
                    Log.LogDebug("Exception ignored: {0}", e.Message);
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private async Task WaitServiceStarted()
    {
        while (true)
        {
            await _lock.WaitAsync(); // 异步锁定
            try
            {
                var service = typeof(ChromeDriver).GetProperty("Service", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(_driver);
                var process = service?.GetType().GetProperty("Process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(service);
                var pollMethod = process?.GetType().GetMethod("Poll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (pollMethod != null && pollMethod.Invoke(process, null) != null)
                {
                    await Task.Delay(250); // 使用默认的 0.25 秒延迟
                }
                else
                {
                    break;
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
