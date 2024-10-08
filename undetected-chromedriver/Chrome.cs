using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Internal;
using OpenQA.Selenium.Remote;

public class Chrome : OpenQA.Selenium.Chrome.ChromeDriver
{
    private static HashSet<Chrome> _instances = new HashSet<Chrome>();
    private string sessionId;
    private bool debug;
    private Patcher patcher;
    private Reactor reactor;
    private string userDataDir;
    private bool keepUserDataDir;
    private int browserPid;
    private ChromeOptions options;

    public Chrome(
        ChromeOptions options = null,
        string userDataDir = null,
        string driverExecutablePath = null,
        string browserExecutablePath = null,
        int port = 0,
        bool enableCdpEvents = false,
        bool advancedElements = false,
        bool keepAlive = true,
        int logLevel = 0,
        bool headless = false,
        int? versionMain = null,
        bool patcherForceClose = false,
        bool suppressWelcome = true,
        bool useSubprocess = true,
        bool debug = false,
        bool noSandbox = true,
        bool userMultiProcs = false
    )
    {
        // Finalizer to ensure resources are released
        GC.ReRegisterForFinalize(this);
        this.debug = debug;
        options = ChromeOptions(options, userDataDir, driverExecutablePath, browserExecutablePath, port, enableCdpEvents, logLevel, headless, versionMain, patcherForceClose, suppressWelcome, noSandbox, userMultiProcs);

        // Use subprocess to start browser or not
        if (useSubprocess)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = options.BinaryLocation,
                Arguments = string.Join(" ", options.Arguments),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            this.browserPid = process.Id;
        }
        else
        {
            this.browserPid = StartDetached(options.BinaryLocation, options.Arguments.ToArray());
        }

        var service = ChromeDriverService.CreateDefaultService(patcher.ExecutablePath);



        this.reactor = null;

        // Initialize Reactor if CDP events are enabled
        if (enableCdpEvents)
        {
            var reactor = new Reactor(this);
            reactor.StartAsync();
            this.reactor = reactor;
        }

        //TODO _web_element_cls
        //if (advancedElements)
        //{
        //    this.WebElementFactory =new UCWebElement();
        //}
        //else
        //{
        //    this.WebElementFactory = WebElement.Factory;
        //}

        if (headless || options.Arguments.Contains("headless"))
        {
            this.ConfigureHeadless();
        }
    }

    private ChromeOptions ChromeOptions(ChromeOptions options, string userDataDir, string driverExecutablePath,
        string browserExecutablePath, int port, bool enableCdpEvents, int logLevel, bool headless, int? versionMain,
        bool patcherForceClose, bool suppressWelcome, bool noSandbox, bool userMultiProcs)
    {
        this.patcher = new Patcher(driverExecutablePath, patcherForceClose, versionMain ?? 0, userMultiProcs);
        this.patcher.Auto();

        // Handle options if not provided
        if (options == null)
        {
            options = new ChromeOptions();
        }

        string debugHost = "";
        int debugPort = 0;
        if (options.DebuggerAddress == null)
        {
            debugHost = "127.0.0.1";
            debugPort = port != 0 ? port : PortUtilities.FindFreePort();
            options.DebuggerAddress = $"{debugHost}:{debugPort}";
        }
        else
        {
            debugHost = options.DebuggerAddress.Split(":")[0];
            debugPort = int.Parse(options.DebuggerAddress.Split(":")[1]);
        }

        if (enableCdpEvents)
        {
            //TODO SetCapability
            //options.SetCapability("goog:loggingPrefs", new Dictionary<string, string>
            //{
            //    { "performance", "ALL" },
            //    { "browser", "ALL" }
            //});
        }

        options.AddArgument($"--remote-debugging-host={debugHost}");
        options.AddArgument($"--remote-debugging-port={debugPort}");

        if (!string.IsNullOrEmpty(userDataDir))
        {
            options.AddArgument($"--user-data-dir={userDataDir}");
        }

        this.userDataDir = userDataDir ?? CreateTempUserDataDir();
        this.keepUserDataDir = !string.IsNullOrEmpty(userDataDir);

        // Set language options
        string language = null;
        foreach (var arg in options.Arguments)
        {
            //if (arg.Contains("--headless"))
            //{
            //    options.Arguments.Remove(arg);
            //    options.Headless = true;
            //}
            if (arg.Contains("lang"))
            {
                var match = Regex.Match(arg, "(?:--)?lang(?:[ =])?(.*)");
                if (match.Success)
                {
                    language = match.Groups[1].Value;
                }
            }
            if (arg.Contains("user-data-dir"))
            {
                var match = Regex.Match(arg, "(?:--)?user-data-dir(?:[ =])?(.*)");
                if (match.Success)
                {
                    this.userDataDir = match.Groups[1].Value;
                    this.keepUserDataDir = true;
                }
            }
        }

        if (string.IsNullOrEmpty(this.userDataDir))
        {
            this.userDataDir = Path.GetTempPath();
            this.keepUserDataDir = false;
            options.AddArgument($"--user-data-dir={this.userDataDir}");
        }

        if (string.IsNullOrEmpty(language))
        {
            try
            {
                language = CultureInfo.CurrentCulture.Name.Replace('_', '-');
            }
            catch
            {
                language = "en-US";
            }
        }

        options.AddArgument($"--lang={language}");

        if (string.IsNullOrEmpty(options.BinaryLocation))
        {
            options.BinaryLocation = browserExecutablePath ?? FindChromeExecutable();
        }

        if (string.IsNullOrEmpty(options.BinaryLocation) || !File.Exists(options.BinaryLocation))
        {
            throw new FileNotFoundException("Could not determine browser executable.");
        }

        if (suppressWelcome)
        {
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--no-first-run");
        }

        if (noSandbox)
        {
            options.AddArgument("--no-sandbox");
            options.AddArgument("--test-type");
        }

        if (headless || options.Arguments.Contains("headless"))
        {
            try
            {
                options.AddArgument(versionMain < 108 ? "--headless=chrome" : "--headless=new");
            }
            catch
            {
                options.AddArgument("--headless=new");
            }
        }

        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--start-maximized");

        options.AddArgument($"--log-level={logLevel}");

        // Handle preferences if exists
        if (options.Arguments.Contains("handle_prefs"))
        {
            options.HandlePrefs(userDataDir);
        }

        // Fix exit_type flag to prevent tab-restore nag
        try
        {
            var prefsFile = Path.Combine(userDataDir, "Default", "Preferences");
            using (var fs = new FileStream(prefsFile, FileMode.Open, FileAccess.ReadWrite))
            using (var reader = new StreamReader(fs, System.Text.Encoding.Latin1))
            using (var writer = new StreamWriter(fs))
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                if (config["profile"] is Dictionary<string, object> profile && profile.ContainsKey("exit_type"))
                {
                    profile["exit_type"] = null;
                    fs.Seek(0, SeekOrigin.Begin);
                    JsonSerializer.Serialize(fs, config);
                    fs.SetLength(fs.Position);
                }
            }
        }
        catch (Exception)
        {
            Console.WriteLine("Could not find or fix exit_type flag.");
        }

        // Set options and capabilities
        this.options = options;
        return options;
    }

    private int GetFreePort()
    {
        // You can implement your logic to get a free port here
        return 0;
    }

    private string CreateTempUserDataDir()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    private string FindChromeExecutable()
    {
        // Your implementation to find the Chrome executable
        return null;
    }

    private int StartDetached(string fileName, string[] arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = string.Join(" ", arguments),
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return process.Id;
    }

    private void ConfigureHeadless()
    {
        // Implement the logic to configure headless mode
    }
}
