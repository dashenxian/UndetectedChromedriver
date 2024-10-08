using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class Patcher
{
    private static Mutex mutex = new Mutex();
    private string exeName = "chromedriver";
    private string platform;
    private string dataPath;
    public string ExecutablePath;
    private string zipPath;
    private bool isOldChromeDriver;
    private bool userMultiProcs;
    private string urlRepo;

    public Patcher(string executablePath = null, bool force = false, int versionMain = 0, bool userMultiProcs = false)
    {
        platform = Environment.OSVersion.Platform.ToString().ToLower();
        isOldChromeDriver = versionMain > 0 && versionMain <= 114;
        userMultiProcs = userMultiProcs;

        SetPlatformName();

        dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "undetected_chromedriver");
        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }

        if (executablePath == null)
        {
            this.ExecutablePath = Path.Combine(dataPath, "undetected_" + exeName);
        }
        else
        {
            this.ExecutablePath = executablePath;
        }

        zipPath = Path.Combine(dataPath, "undetected");

        if (isOldChromeDriver)
        {
            urlRepo = "https://chromedriver.storage.googleapis.com";
        }
        else
        {
            urlRepo = "https://googlechromelabs.github.io/chrome-for-testing";
        }
    }

    private void SetPlatformName()
    {
        if (platform.Contains("win"))
        {
            exeName += ".exe";
        }
        else if (platform.Contains("linux"))
        {
            exeName += "";
        }
        else if (platform.Contains("mac"))
        {
            exeName += "";
        }
    }

    public async Task<bool> Auto(string executablePath = null, bool force = false, int versionMain = 0)
    {
        if (userMultiProcs)
        {
            CleanupUnusedFiles();
        }

        if (executablePath != null)
        {
            this.ExecutablePath = executablePath;
        }

        string releaseNumber = await FetchReleaseNumber();

        if (releaseNumber == null)
        {
            return false;
        }

        await UnzipPackage(await FetchPackage());

        return PatchExe();
    }

    private async Task<string> FetchReleaseNumber()
    {
        using (HttpClient client = new HttpClient())
        {
            string path = isOldChromeDriver ? $"/latest_release_{exeName}" : "/last-known-good-versions-with-downloads.json";
            string response = await client.GetStringAsync(urlRepo + path);
            return response;
        }
    }

    private async Task<string> FetchPackage()
    {
        using (HttpClient client = new HttpClient())
        {
            string zipName = $"chromedriver_{platform}.zip";
            string downloadUrl = $"{urlRepo}/{exeName}/{zipName}";
            string tempFile = Path.GetTempFileName();
            byte[] data = await client.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempFile, data);
            return tempFile;
        }
    }

    private async Task UnzipPackage(string zipFilePath)
    {
        string extractPath = Path.Combine(dataPath, exeName);
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, true);
        }

        ZipFile.ExtractToDirectory(zipFilePath, dataPath);
        await Task.Run(() => File.Move(Path.Combine(dataPath, exeName), ExecutablePath));
    }

    private bool PatchExe()
    {
        string content = File.ReadAllText(ExecutablePath);
        if (!content.Contains("undetected chromedriver"))
        {
            // Perform the necessary patching here.
            File.WriteAllText(ExecutablePath, content.Replace("window.cdc", "undetected chromedriver"));
            return true;
        }
        return false;
    }

    private void CleanupUnusedFiles()
    {
        foreach (var file in Directory.GetFiles(dataPath, "*undetected*"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Handle exception
            }
        }
    }
}
