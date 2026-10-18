param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$probeRoot = $OutputDirectory
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
public static class BackgroundFocusProbe {
    delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; public uint reserved; }
    static StreamWriter writer;
    static void Record(string kind, IntPtr hwnd) {
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        string name = "unknown";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch {}
        writer.WriteLine(DateTime.UtcNow.ToString("o") + "\t" + kind + "\t" + hwnd.ToInt64() + "\t" + pid + "\t" + name);
    }
    public static void Run(string root) {
        using(writer = new StreamWriter(Path.Combine(root,"foreground.tsv"),false)) {
            writer.AutoFlush = true;
            writer.WriteLine("utc\tkind\thwnd\tpid\tprocess");
            WinEventDelegate callback = (h,e,w,o,c,t,ms) => Record("event",w);
            IntPtr hook = SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
            if(hook == IntPtr.Zero) throw new Exception("Cannot install passive foreground event hook.");
            try {
                IntPtr last = GetForegroundWindow(); Record("start",last);
                if(last == IntPtr.Zero) throw new Exception("No foreground window visible; cannot provide valid evidence.");
                File.WriteAllText(Path.Combine(root,"ready.txt"),DateTime.UtcNow.ToString("o"));
                Console.WriteLine("Passive foreground monitor ready; no activation APIs used.");
                var timer = Stopwatch.StartNew();
                while(timer.Elapsed.TotalSeconds < 180 && !File.Exists(Path.Combine(root,"stop.txt"))) {
                    MSG msg; while(PeekMessage(out msg,IntPtr.Zero,0,0,1)) {}
                    IntPtr current=GetForegroundWindow();
                    if(current!=last) { Record("sample-change",current); last=current; }
                    Thread.Sleep(10);
                }
                Record("end",GetForegroundWindow());
            } finally { UnhookWinEvent(hook); GC.KeepAlive(callback); }
        }
    }
}
'@
[BackgroundFocusProbe]::Run($probeRoot)
