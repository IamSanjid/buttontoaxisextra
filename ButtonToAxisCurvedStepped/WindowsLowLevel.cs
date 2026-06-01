using HidWizards.UCR.Core.Utilities;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;

public static class WindowsLowLevel
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    // Native Win32 API constants
    private const int ProcessPowerThrottling = 8;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;

    // Flags to target both speed and timer resolution restrictions
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static int GetLastWin32Error()
    {
        return Marshal.GetLastWin32Error();
    }

    public static bool DisablePowerThrottling()
    {
        int build = int.Parse(Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "CurrentBuild", "0").ToString());
        if (build < 22000)
        {
            Logger.Info("Windows 10 detected, skipping power throttling API (not supported)");
            return true;
        }

        return TrySetProcessPowerThrottling();
    }

    private static bool TrySetProcessPowerThrottling()
    {
        IntPtr currentProcess = GetCurrentProcess();

        PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            StateMask = 0
        };

        bool result = SetProcessInformation(
            currentProcess,
            ProcessPowerThrottling,
            ref state,
            (uint)Marshal.SizeOf(state));

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();

            // 183 = already in desired state, treat as success
            if (error == 183)
            {
                Logger.Info("Power throttling already disabled");
                return true;
            }

            // Try fallback with EXECUTION_SPEED only (older Windows 11 builds)
            Logger.Warn($"Full mask failed (error {error}), trying EXECUTION_SPEED only");

            state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;

            result = SetProcessInformation(
                currentProcess,
                ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf(state));

            if (!result)
            {
                error = Marshal.GetLastWin32Error();
                if (error == 183) return true;

                Logger.Warn($"Failed to disable power throttling, error: {error}");
                return false;
            }
        }

        return true;
    }

    private static readonly ThreadLocal<Stopwatch> _waitSwTL = new ThreadLocal<Stopwatch>();
    public static void PreciseWait(double targetMilliseconds)
    {
        if (!_waitSwTL.IsValueCreated)
        {
            _waitSwTL.Value = new Stopwatch();
        }
        var sw = _waitSwTL.Value;
        sw.Restart();

        int sleepTime = (int)(targetMilliseconds - 4.0);

        if (sleepTime > 0)
            Thread.Sleep(sleepTime);

        // Check for overshoot
        if (sw.Elapsed.TotalMilliseconds >= targetMilliseconds) return;

        // Middle zone: still > 1ms left, yield cheaply
        while (targetMilliseconds - sw.Elapsed.TotalMilliseconds > 1.0)
        {
            Thread.Sleep(0); // yield, don't burn core yet
        }

        // Final zone: < 1ms left, pure spin — no yielding
        while (sw.Elapsed.TotalMilliseconds < targetMilliseconds)
        {
            // Empty or Thread.SpinWait(1) — stay hot on this core
        }
    }
}
