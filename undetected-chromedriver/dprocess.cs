using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

public static class ProcessManager
{
    private static readonly List<int> RegisteredPids = new List<int>();

    public static int StartDetached(string executable, params string[] args)
    {
        // Create a new process
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", args),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Configure for Windows
        };

        // On Windows, set additional flags
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            processStartInfo.CreateNoWindow = true;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        // Start the process
        var process = Process.Start(processStartInfo);
        if (process != null)
        {
            RegisteredPids.Add(process.Id);
            return process.Id;
        }
        throw new InvalidOperationException("Could not start the process.");
    }

    // Cleanup registered processes
    private static void Cleanup()
    {
        foreach (var pid in RegisteredPids.ToList())
        {
            try
            {
                var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up PID {pid}: {ex.Message}");
            }
        }
    }

    // Register the cleanup method to be called on exit
    static ProcessManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
    }
}