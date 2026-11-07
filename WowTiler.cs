// =============================================================================
//  WoW Window Tiler  (WowTiler.exe)
// =============================================================================
//  Finds every running World of Warcraft client window, tiles them on one
//  monitor (one big "main" slot + smaller alt slots, or an even grid), and lets
//  you swap any client into the main slot with a button or a global hotkey.
//  Clients that log in or out are picked up automatically. Every client keeps
//  a 16:9 game area (or 16:10 / 21:9 / 4:3 / free, your choice).
//
//  It only moves/resizes windows. It never sends keys or clicks to the game.
//
//  BUILD:  double-click build.bat (uses the C# compiler that ships with Windows)
//  RUN:    WowTiler.exe   -- settings are saved next to it in WowTiler.ini
//
//  WoW must be in *Windowed* mode (System > Graphics > Display Mode).
//  Exclusive Fullscreen can't be moved, and "Windowed (Fullscreen)" fights it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WowTiler
{
    // -------------------------------------------------------------------------
    // Win32
    // -------------------------------------------------------------------------
    static class Native
    {
        public const int GWL_STYLE = -16;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_MINIMIZEBOX = 0x00020000L;
        public const long WS_MAXIMIZEBOX = 0x00010000L;
        public const long WS_SYSMENU = 0x00080000L;
        public const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;
        public const long BORDER_BITS = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const int SW_RESTORE = 9;

        public const int WM_HOTKEY = 0x0312;
        public const uint WM_SIZE = 0x0005, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const int SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7;
        public const uint WM_SYSCOMMAND = 0x0112, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        public const int SC_SIZE = 0xF000, WMSZ_BOTTOMRIGHT = 8, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
        public const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x0001;
        public const int ERROR_ACCESS_DENIED = 5;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        public static readonly uint WM_SHOWME = RegisterWindowMessage("WowTiler.ShowMe");

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0x0000;
        public const int VK_SHIFT = 0x10;
        public const int ASFW_ANY = -1;
        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        public const uint SWP_SHOWWINDOW = 0x0040;

        public static void BringToTop(IntPtr h)
        {
            SetWindowPos(h, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        // Set when Windows refuses to let us talk to a WoW window (UIPI: WoW is elevated, we are not).
        public static bool AccessDenied;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public const byte VK_MENU = 0x12;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int idx, int val);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr val);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeoutMs, out IntPtr result);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public MOUSEINPUT mi; }

        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        static void NudgeMouse()
        {
            var inp = new INPUT[1];
            inp[0].type = INPUT_MOUSE;
            inp[0].mi.dwFlags = MOUSEEVENTF_MOVE;   // zero-length move: just generates a WM_MOUSEMOVE
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        // A genuine resize through Windows' own size/move loop -- exactly what a mouse
        // drag of the corner does, so the game sees the real WM_SIZING / WM_EXITSIZEMOVE
        // sequence. Moves the cursor for ~150 ms per window and puts it back.
        public static void DragResize(IntPtr h, Rectangle r)
        {
            RECT cur;
            if (!GetWindowRect(h, out cur)) { Move(h, r); return; }
            int curW = cur.Right - cur.Left, curH = cur.Bottom - cur.Top;
            Move(h, new Rectangle(r.X, r.Y, curW, curH));           // top-left corner first (position is never the problem)
            if (curW == r.Width && curH == r.Height) return;
            if ((GetStyle(h) & WS_THICKFRAME) == 0) { Move(h, r); return; }   // no sizable frame: the loop would refuse

            POINT saved; GetCursorPos(out saved);
            PostMessage(h, WM_SYSCOMMAND, new IntPtr(SC_SIZE + WMSZ_BOTTOMRIGHT), IntPtr.Zero);
            System.Threading.Thread.Sleep(80);                        // the loop starts and warps the cursor to the corner
            POINT start; GetCursorPos(out start);
            SetCursorPos(start.X + (r.Width - curW), start.Y + (r.Height - curH));
            NudgeMouse();
            System.Threading.Thread.Sleep(50);
            PostMessage(h, WM_KEYDOWN, new IntPtr(VK_RETURN), IntPtr.Zero);   // accept the new size
            PostMessage(h, WM_KEYUP, new IntPtr(VK_RETURN), IntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            SetCursorPos(saved.X, saved.Y);
            Move(h, r);                                               // snap any 1-2 px drift from the frame offset
        }

        // How much bigger the outer window is than its client (game) area:
        // title bar + frame when bordered, ~0 when borderless.
        public static bool TryGetWindowRect(IntPtr h, out Rectangle r)
        {
            RECT w;
            if (!GetWindowRect(h, out w)) { r = Rectangle.Empty; return false; }
            r = new Rectangle(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
            return true;
        }

        public static void FrameSize(IntPtr h, out int frameW, out int frameH)
        {
            frameW = 0; frameH = 0;
            RECT w, c;
            if (!GetWindowRect(h, out w) || !GetClientRect(h, out c)) return;
            frameW = Math.Max(0, (w.Right - w.Left) - (c.Right - c.Left));
            frameH = Math.Max(0, (w.Bottom - w.Top) - (c.Bottom - c.Top));
        }

        // Apply a pending style change (border on/off) without moving the window,
        // so FrameSize measures the new frame.
        public static void CommitFrame(IntPtr h)
        {
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        // Make Windows give us real pixels on scaled (125%/150%) displays.
        public static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwareness(2); return; } catch (Exception) { }
            try { SetProcessDPIAware(); } catch (Exception) { }
        }

        public static long GetStyle(IntPtr h)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(h, GWL_STYLE).ToInt64();
            return unchecked((uint)GetWindowLong32(h, GWL_STYLE));
        }

        public static void SetStyle(IntPtr h, long style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_STYLE, new IntPtr(style));
            else SetWindowLong32(h, GWL_STYLE, unchecked((int)style));
        }

        static readonly Dictionary<uint, bool> processCache = new Dictionary<uint, bool>();

        // Wow.exe / WowClassic.exe / WowT.exe ... Anything we can't inspect (elevated) is allowed through.
        static bool IsWowProcess(uint pid)
        {
            bool ok;
            if (processCache.TryGetValue(pid, out ok)) return ok;
            try
            {
                string name = Process.GetProcessById((int)pid).ProcessName;
                ok = name.StartsWith("wow", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { ok = true; }
            if (processCache.Count > 500) processCache.Clear();
            processCache[pid] = ok;
            return ok;
        }

        public static List<WowWindow> FindWowWindows()
        {
            var list = new List<WowWindow>();
            var sb = new StringBuilder(256);
            EnumWindowsProc cb = delegate(IntPtr h, IntPtr lp)
            {
                if (!IsWindowVisible(h)) return true;
                sb.Length = 0; GetClassName(h, sb, 256); string cls = sb.ToString();
                sb.Length = 0; GetWindowText(h, sb, 256); string title = sb.ToString();
                if (cls != "GxWindowClass" && !title.StartsWith("World of Warcraft", StringComparison.Ordinal)) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!IsWowProcess(pid)) return true;                             // drops browser tabs with WoW titles
                list.Add(new WowWindow { Hwnd = h, Pid = pid, Title = title });
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        static bool Tell(IntPtr h, uint msg, IntPtr wp, IntPtr lp)
        {
            IntPtr result;
            IntPtr ok = SendMessageTimeout(h, msg, wp, lp, SMTO_ABORTIFHUNG, 1500, out result);
            if (ok == IntPtr.Zero && Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED) AccessDenied = true;
            return ok != IntPtr.Zero;
        }

        static void SizeMsg(IntPtr h)
        {
            RECT c;
            if (!GetClientRect(h, out c)) return;
            int cw = c.Right - c.Left, ch = c.Bottom - c.Top;
            Tell(h, WM_SIZE, IntPtr.Zero, new IntPtr((ch << 16) | (cw & 0xFFFF)));
        }

        const uint PlaceFlags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED;

        static void Move(IntPtr h, Rectangle r)
        {
            if (!SetWindowPos(h, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, PlaceFlags)
                && Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED) AccessDenied = true;
        }

        // Resize methods. WoW decides on its own when to rebuild its render target
        // after the window changes size; which event it keys on isn't documented,
        // so several ways of nudging it are offered.
        //   0 = drag messages: ENTERSIZEMOVE, resize, WM_SIZE, EXITSIZEMOVE (like a mouse drag)
        //   1 = two-step: resize to 1px short, then to the final size (for engines that debounce)
        //   2 = minimize/restore after the resize (forces a full restore cycle)
        //   3 = plain SetWindowPos only
        public static void Place(IntPtr h, Rectangle r, int method)
        {
            if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, SW_RESTORE);
            switch (method)
            {
                case 1:
                    Tell(h, WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    Move(h, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1));
                    System.Threading.Thread.Sleep(120);
                    Move(h, r);
                    SizeMsg(h);
                    Tell(h, WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    break;
                case 2:
                    Move(h, r);
                    ShowWindow(h, SW_SHOWMINNOACTIVE);
                    ShowWindow(h, SW_SHOWNOACTIVATE);
                    Move(h, r);
                    break;
                case 3:
                    Move(h, r);
                    break;
                case 4:
                    DragResize(h, r);
                    break;
                default:
                    Tell(h, WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    Move(h, r);
                    SizeMsg(h);
                    Tell(h, WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    break;
            }
        }

        // SetForegroundWindow may refuse a background app; tapping ALT makes
        // Windows treat us as the active input thread first.
        public static bool ForceForeground(IntPtr h)
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            bool ok = SetForegroundWindow(h);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            return ok;
        }
    }

    class WowWindow
    {
        public IntPtr Hwnd;
        public uint Pid;
        public string Title = "";
        public string Label = "";
        public bool HasOrigStyle;
        public long OrigStyle;
        public string ExeDir = "";          // folder of WowClassic.exe (empty if unreadable)
        public int LoginIndex = 0;          // order in which this client was first seen this session
        public int PromoteTick = 0;         // when it was last promoted to a main slot (for multi-main rotation)
        public int Maximized = -1;          // gxMaximize from Config.wtf: 1 = "Windowed (Fullscreen)", 0 = Windowed, -1 = unknown
        public bool Fullscreen;             // gxWindow "0": exclusive fullscreen (older clients); can't be tiled at all
        public bool Ignored;                // user asked us to leave this client alone
        public bool NeedsFix { get { return Maximized == 1 || Fullscreen; } }

        public string ConfigPath { get { return ExeDir.Length == 0 ? "" : Path.Combine(Path.Combine(ExeDir, "WTF"), "Config.wtf"); } }

        public void ReadConfig()
        {
            Maximized = -1;
            try
            {
                if (ExeDir.Length == 0) { ExeDir = Path.GetDirectoryName(Process.GetProcessById((int)Pid).MainModule.FileName); }
                string cfg = ConfigPath;
                if (!File.Exists(cfg)) return;
                foreach (string line in File.ReadAllLines(cfg))
                {
                    string t = line.Trim();
                    if (t.StartsWith("SET gxMaximize ", StringComparison.OrdinalIgnoreCase))
                        Maximized = t.IndexOf("\"1\"") >= 0 ? 1 : 0;
                    else if (t.StartsWith("SET gxWindow ", StringComparison.OrdinalIgnoreCase))
                        Fullscreen = t.IndexOf("\"0\"") >= 0;
                }
            }
            catch (Exception) { }
        }

        // Rewrites Config.wtf so the client starts in plain Windowed mode next time.
        public bool SetWindowedInConfig()
        {
            try
            {
                string cfg = ConfigPath;
                if (cfg.Length == 0 || !File.Exists(cfg)) return false;
                var lines = new List<string>(File.ReadAllLines(cfg));
                bool foundMax = false, foundWin = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("SET gxMaximize ", StringComparison.OrdinalIgnoreCase)) { lines[i] = "SET gxMaximize \"0\""; foundMax = true; }
                    else if (t.StartsWith("SET gxWindow ", StringComparison.OrdinalIgnoreCase)) { lines[i] = "SET gxWindow \"1\""; foundWin = true; }
                }
                if (!foundMax) lines.Add("SET gxMaximize \"0\"");
                if (!foundWin) lines.Add("SET gxWindow \"1\"");
                File.Copy(cfg, cfg + ".wowtiler-backup", true);
                File.WriteAllLines(cfg, lines.ToArray());
                Maximized = 0; Fullscreen = false;
                return true;
            }
            catch (Exception) { return false; }
        }

        // Returns true when the window style actually changed.
        public bool SetBorderless(bool on)
        {
            long style = Native.GetStyle(Hwnd);
            long now;
            if (on)
            {
                if (!HasOrigStyle) { OrigStyle = style; HasOrigStyle = true; }
                now = style & ~Native.BORDER_BITS;
            }
            else
            {
                now = HasOrigStyle ? OrigStyle : (style | Native.WS_OVERLAPPEDWINDOW);
                HasOrigStyle = false;
            }
            if (now == style) return false;
            Native.SetStyle(Hwnd, now);
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Config (plain key=value file next to the exe)
    // -------------------------------------------------------------------------
    class Config
    {
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WowTiler.ini");

        public string Layout = "main_left";     // main_left | main_right | main_top | main_bottom | grid
        public string Aspect = "16:9";          // 16:9 | 16:10 | 21:9 | 4:3 | free
        public int Monitor = 0;
        public int MainPercent = 70;
        public int Mains = 1;                   // how many big slots (used by the "Mains ..." layouts and Custom)
        public int Gap = 0;
        public bool CoverTaskbar = false;
        public bool FillScreen = true;          // main absorbs leftover space so nothing is empty
        public int ResizeMethod = 0;            // see Native.Place
        public bool RaidMinimizeAlts = false;   // raid mode: minimize alts instead of leaving them behind the main
        public bool StartMinimized = false;     // start hidden in the tray (used by the Windows-startup shortcut)
        public bool SwapOnFocus = true;         // clicking into an alt makes it the main
        public string SwapModifier = "shift";   // shift | ctrl | alt | ctrl+shift | none
        public bool CheckUpdates = true;        // look for a newer release at startup
        public int AltMonitor = -1;             // -1 = same monitor as the main; otherwise monitor index for the alts
        public List<RectangleF> CustomSlots = new List<RectangleF>();   // "custom" layout: slots as fractions of the canvas, slot 0 = main
        public int ReserveTop = 0, ReserveBottom = 0, ReserveLeft = 0, ReserveRight = 0;   // px kept free at the edges
        public List<string> Labels = new List<string>();   // remembered client labels, in login order
        public bool AutoApply = true;
        public bool Borderless = false;
        public bool FocusOnSwap = true;
        public bool AlwaysOnTop = true;
        public Dictionary<string, string> Hotkeys = new Dictionary<string, string>();

        public static readonly string[] HotkeyNames = {
            "next", "prev", "retile", "raid", "slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7", "slot8", "slot9" };

        public Config()
        {
            Hotkeys["next"] = "ctrl+alt+right";
            Hotkeys["prev"] = "ctrl+alt+left";
            Hotkeys["retile"] = "ctrl+alt+home";
            Hotkeys["raid"] = "ctrl+alt+end";
            for (int i = 1; i <= 9; i++) Hotkeys["slot" + i] = "ctrl+alt+" + i;
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    int n; bool b;
                    switch (key)
                    {
                        case "layout": c.Layout = val; break;
                        case "aspect": c.Aspect = val; break;
                        case "monitor": if (int.TryParse(val, out n)) c.Monitor = n; break;
                        case "main_percent": if (int.TryParse(val, out n)) c.MainPercent = Math.Max(40, Math.Min(90, n)); break;
                        case "mains": if (int.TryParse(val, out n)) c.Mains = Math.Max(1, Math.Min(4, n)); break;
                        case "gap": if (int.TryParse(val, out n)) c.Gap = Math.Max(0, Math.Min(60, n)); break;
                        case "cover_taskbar": if (bool.TryParse(val, out b)) c.CoverTaskbar = b; break;
                        case "fill_screen": if (bool.TryParse(val, out b)) c.FillScreen = b; break;
                        case "raid_minimize_alts": if (bool.TryParse(val, out b)) c.RaidMinimizeAlts = b; break;
                        case "start_minimized": if (bool.TryParse(val, out b)) c.StartMinimized = b; break;
                        case "swap_on_focus": if (bool.TryParse(val, out b)) c.SwapOnFocus = b; break;
                        case "swap_modifier": c.SwapModifier = val.ToLowerInvariant(); break;
                        case "check_updates": if (bool.TryParse(val, out b)) c.CheckUpdates = b; break;
                        case "alt_monitor": if (int.TryParse(val, out n)) c.AltMonitor = n; break;
                        case "custom_slots":
                            c.CustomSlots.Clear();
                            foreach (string part in val.Split(';'))
                            {
                                string[] f = part.Split(',');
                                float x, y, w, h;
                                if (f.Length == 4
                                    && float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                                    && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                                    && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out w)
                                    && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                                    c.CustomSlots.Add(new RectangleF(x, y, w, h));
                            }
                            break;
                        case "reserve_top": if (int.TryParse(val, out n)) c.ReserveTop = Math.Max(0, n); break;
                        case "reserve_bottom": if (int.TryParse(val, out n)) c.ReserveBottom = Math.Max(0, n); break;
                        case "reserve_left": if (int.TryParse(val, out n)) c.ReserveLeft = Math.Max(0, n); break;
                        case "reserve_right": if (int.TryParse(val, out n)) c.ReserveRight = Math.Max(0, n); break;
                        case "labels": c.Labels = new List<string>(val.Split('|')); break;
                        case "resize_method": if (int.TryParse(val, out n)) c.ResizeMethod = Math.Max(0, Math.Min(4, n)); break;
                        case "auto_apply": if (bool.TryParse(val, out b)) c.AutoApply = b; break;
                        case "borderless": if (bool.TryParse(val, out b)) c.Borderless = b; break;
                        case "focus_on_swap": if (bool.TryParse(val, out b)) c.FocusOnSwap = b; break;
                        case "always_on_top": if (bool.TryParse(val, out b)) c.AlwaysOnTop = b; break;
                        default:
                            if (key.StartsWith("hotkey.")) c.Hotkeys[key.Substring(7)] = val;
                            break;
                    }
                }
            }
            catch (Exception) { }
            return c;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# WoW Window Tiler settings. Hotkeys look like ctrl+alt+right, ctrl+shift+f5, alt+numpad1.");
                sb.AppendLine("layout=" + Layout);
                sb.AppendLine("aspect=" + Aspect);
                sb.AppendLine("monitor=" + Monitor);
                sb.AppendLine("main_percent=" + MainPercent);
                sb.AppendLine("mains=" + Mains);
                sb.AppendLine("gap=" + Gap);
                sb.AppendLine("cover_taskbar=" + CoverTaskbar);
                sb.AppendLine("fill_screen=" + FillScreen);
                sb.AppendLine("resize_method=" + ResizeMethod);
                sb.AppendLine("raid_minimize_alts=" + RaidMinimizeAlts);
                sb.AppendLine("start_minimized=" + StartMinimized);
                sb.AppendLine("swap_on_focus=" + SwapOnFocus);
                sb.AppendLine("swap_modifier=" + SwapModifier);
                sb.AppendLine("check_updates=" + CheckUpdates);
                sb.AppendLine("alt_monitor=" + AltMonitor);
                var parts = new List<string>();
                foreach (RectangleF r in CustomSlots)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4},{3:F4}", r.X, r.Y, r.Width, r.Height));
                sb.AppendLine("custom_slots=" + string.Join(";", parts.ToArray()));
                sb.AppendLine("reserve_top=" + ReserveTop);
                sb.AppendLine("reserve_bottom=" + ReserveBottom);
                sb.AppendLine("reserve_left=" + ReserveLeft);
                sb.AppendLine("reserve_right=" + ReserveRight);
                sb.AppendLine("labels=" + string.Join("|", Labels.ToArray()));
                sb.AppendLine("auto_apply=" + AutoApply);
                sb.AppendLine("borderless=" + Borderless);
                sb.AppendLine("focus_on_swap=" + FocusOnSwap);
                sb.AppendLine("always_on_top=" + AlwaysOnTop);
                foreach (string name in HotkeyNames)
                    if (Hotkeys.ContainsKey(name)) sb.AppendLine("hotkey." + name + "=" + Hotkeys[name]);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception) { }
        }
    }

    // -------------------------------------------------------------------------
    // Key recorder: click the box, press a combination, it becomes the binding.
    // -------------------------------------------------------------------------
    class KeyRecorder : TextBox
    {
        public event EventHandler BindingChanged;
        public string Binding = "";

        public KeyRecorder()
        {
            ReadOnly = true; Cursor = Cursors.Hand; TextAlign = HorizontalAlignment.Center;
            Font = new Font("Consolas", 9.5F);
            GotFocus += delegate { Text = "press keys..."; };
            LostFocus += delegate { Text = Binding.Length == 0 ? "(none)" : Binding; };
        }

        public void Set(string binding) { Binding = binding ?? ""; Text = Binding.Length == 0 ? "(none)" : Binding; }

        protected override bool IsInputKey(Keys keyData) { return true; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { return false; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true; e.SuppressKeyPress = true;
            Keys k = e.KeyCode;
            if (k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;   // wait for the real key
            if (k == Keys.Back || k == Keys.Delete) { Apply(""); return; }
            if (k == Keys.Escape) { Parent.Focus(); return; }
            string name = KeyName(k);
            if (name == null) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("ctrl");
            if (e.Alt) parts.Add("alt");
            if (e.Shift) parts.Add("shift");
            parts.Add(name);
            Apply(string.Join("+", parts.ToArray()));
        }

        void Apply(string b)
        {
            Binding = b; Text = b.Length == 0 ? "(none)" : b;
            if (BindingChanged != null) BindingChanged(this, EventArgs.Empty);
            if (Parent != null) Parent.Focus();
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return k.ToString().ToLowerInvariant();
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return "f" + (int)(k - Keys.F1 + 1);
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "numpad" + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Left: return "left"; case Keys.Right: return "right"; case Keys.Up: return "up"; case Keys.Down: return "down";
                case Keys.Home: return "home"; case Keys.End: return "end"; case Keys.PageUp: return "pageup"; case Keys.PageDown: return "pagedown";
                case Keys.Insert: return "insert"; case Keys.Space: return "space"; case Keys.Tab: return "tab"; case Keys.Return: return "enter";
            }
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // "Start with Windows": a shortcut in the user's Startup folder (no admin needed)
    // -------------------------------------------------------------------------
    static class Startup
    {
        static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WoW Window Tiler.lnk"); }
        }

        public static bool IsEnabled { get { return File.Exists(LinkPath); } }

        public static bool Enable()
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object link = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { LinkPath });
                Type lt = link.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath });
                lt.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "--minimized" });
                lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath + ",0" });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "WoW Window Tiler" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool Disable()
        {
            try { if (File.Exists(LinkPath)) File.Delete(LinkPath); return true; }
            catch (Exception) { return false; }
        }
    }

    // -------------------------------------------------------------------------
    // Hotkey text -> (modifiers, virtual key)
    // -------------------------------------------------------------------------
    static class HotkeyParser
    {
        static readonly Dictionary<string, uint> Named = new Dictionary<string, uint> {
            {"left",0x25},{"up",0x26},{"right",0x27},{"down",0x28},{"home",0x24},{"end",0x23},
            {"pageup",0x21},{"pagedown",0x22},{"insert",0x2D},{"delete",0x2E},{"space",0x20},
            {"tab",0x09},{"enter",0x0D},{"escape",0x1B},{"backspace",0x08} };

        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (text == null) return false;
            foreach (string part in text.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                int n;
                if (p == "ctrl" || p == "control") mods |= Native.MOD_CONTROL;
                else if (p == "alt") mods |= Native.MOD_ALT;
                else if (p == "shift") mods |= Native.MOD_SHIFT;
                else if (p == "win") mods |= Native.MOD_WIN;
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) vk = (uint)char.ToUpperInvariant(p[0]);
                else if (p.Length >= 2 && p[0] == 'f' && int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x6F + n);
                else if (p.StartsWith("numpad") && int.TryParse(p.Substring(6), out n) && n >= 0 && n <= 9) vk = (uint)(0x60 + n);
                else if (Named.ContainsKey(p)) vk = Named[p];
                else return false;
            }
            return vk != 0;
        }
    }

    // -------------------------------------------------------------------------
    // Layout preview / editor. Shows the monitor(s) to scale with one tile per slot.
    //   normal mode: drag a client tile onto another to swap slots; double-click = make main
    //   edit mode:   drag a slot to move it, drag its corner to resize (aspect kept),
    //                snaps to monitor edges and other slots, right-click to delete
    // -------------------------------------------------------------------------
    class LayoutPreview : Control
    {
        public class Tile { public Rectangle Px; public string Label = ""; public IntPtr Hwnd; public bool IsMain; public bool Empty; }

        public List<Rectangle> Monitors = new List<Rectangle>();
        public Rectangle Canvas;
        public List<Tile> Tiles = new List<Tile>();
        public bool EditMode;
        public int MainCount = 1;
        public double Aspect = 16.0 / 9.0;

        public delegate void IndexPair(int a, int b);
        public delegate void IndexOne(int a);
        public event IndexPair SwapRequested;
        public event IndexOne MainRequested;
        public event IndexOne DeleteRequested;
        public event EventHandler SlotsChanged;

        static readonly Color Accent = Color.FromArgb(235, 185, 60), Field = Color.FromArgb(60, 66, 86), Bg = Color.FromArgb(24, 26, 34);
        static readonly Color Line = Color.FromArgb(80, 86, 104), Fg = Color.FromArgb(226, 229, 238), Muted = Color.FromArgb(140, 146, 165);

        float scale = 1f; PointF origin;
        int dragIdx = -1, hoverIdx = -1; bool dragging, resizing; Point dragStart; Rectangle dragOrig; Point mouse;
        const int Grip = 11;

        public LayoutPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Bg;
            Cursor = Cursors.Default;
        }

        void Fit()
        {
            if (Canvas.Width <= 0 || Canvas.Height <= 0) { scale = 1; origin = PointF.Empty; return; }
            float pad = 10;
            scale = Math.Min((Width - 2 * pad) / Canvas.Width, (Height - 2 * pad) / Canvas.Height);
            origin = new PointF((Width - Canvas.Width * scale) / 2 - Canvas.X * scale, (Height - Canvas.Height * scale) / 2 - Canvas.Y * scale);
        }

        RectangleF ToCtl(Rectangle r) { return new RectangleF(origin.X + r.X * scale, origin.Y + r.Y * scale, r.Width * scale, r.Height * scale); }
        int ToScreenDx(int dx) { return (int)Math.Round(dx / scale); }

        int HitTile(Point p)
        {
            for (int i = Tiles.Count - 1; i >= 0; i--) if (ToCtl(Tiles[i].Px).Contains(p)) return i;
            return -1;
        }

        bool HitHandle(int i, Point p)
        {
            RectangleF r = ToCtl(Tiles[i].Px);
            return new RectangleF(r.Right - Grip, r.Bottom - Grip, Grip, Grip).Contains(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Fit();
            Graphics g = e.Graphics;
            g.Clear(Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            for (int m = 0; m < Monitors.Count; m++)
            {
                RectangleF r = ToCtl(Monitors[m]);
                using (var b = new SolidBrush(Color.FromArgb(33, 36, 47))) g.FillRectangle(b, r);
                using (var p = new Pen(Line)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                TextRenderer.DrawText(g, "Monitor " + (m + 1), Font, new Point((int)r.X + 4, (int)r.Y + 2), Muted);
            }

            for (int i = 0; i < Tiles.Count; i++)
            {
                if (dragging && !EditMode && i == dragIdx) continue;
                DrawTile(g, i, ToCtl(Tiles[i].Px), false);
            }
            if (dragging && !EditMode && dragIdx >= 0)
            {
                RectangleF r = ToCtl(Tiles[dragIdx].Px);
                r.Offset(mouse.X - dragStart.X, mouse.Y - dragStart.Y);
                DrawTile(g, dragIdx, r, true);
            }

            if (Tiles.Count == 0 && Monitors.Count > 0)
                TextRenderer.DrawText(g, EditMode ? "No slots yet. Click 'Add slot'." : "Layout preview", Font, ClientRectangle, Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawTile(Graphics g, int i, RectangleF r, bool ghost)
        {
            Tile t = Tiles[i];
            bool hot = i == hoverIdx && dragging && !EditMode && i != dragIdx;
            if (t.Empty)
            {
                using (var p = new Pen(Line) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                if (EditMode)
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(b, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                }
                TextRenderer.DrawText(g, EditMode ? "slot " + (i + 1) : "empty", Font, Rectangle.Round(r), Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Color fill = t.IsMain ? Accent : Field;
                if (ghost) fill = Color.FromArgb(150, fill);
                if (hot) fill = ControlPaint.Light(fill, 0.4f);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                using (var p = new Pen(hot ? Color.White : Bg)) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                Color tc = t.IsMain ? Bg : Fg;
                var tr = Rectangle.Round(r); tr.Inflate(-4, -2);
                string txt = (t.IsMain ? (MainCount > 1 ? "MAIN " + (i + 1) : "MAIN") + "\n" : (i + 1) + "\n") + t.Label;
                TextRenderer.DrawText(g, txt, Font, tr, tc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
            }
            if (EditMode)
            {
                using (var b = new SolidBrush(Accent)) g.FillRectangle(b, r.Right - Grip, r.Bottom - Grip, Grip - 2, Grip - 2);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = HitTile(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (EditMode && i >= 0 && DeleteRequested != null) DeleteRequested(i);
                return;
            }
            if (e.Button != MouseButtons.Left || i < 0) return;
            if (!EditMode && Tiles[i].Empty) return;
            dragIdx = i; dragStart = e.Location; mouse = e.Location; dragOrig = Tiles[i].Px;
            resizing = EditMode && HitHandle(i, e.Location);
            dragging = false;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            if (dragIdx >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - dragStart.X) > 4 || Math.Abs(e.Y - dragStart.Y) > 4)) dragging = true;
                if (dragging)
                {
                    if (EditMode) EditDrag(e.Location);
                    else hoverIdx = HitTile(e.Location);
                    Invalidate();
                }
            }
            else
            {
                int i = HitTile(e.Location);
                Cursor = EditMode && i >= 0 ? (HitHandle(i, e.Location) ? Cursors.SizeNWSE : Cursors.SizeAll)
                       : (i >= 0 && !Tiles[i].Empty ? Cursors.Hand : Cursors.Default);
            }
        }

        void EditDrag(Point p)
        {
            Tile t = Tiles[dragIdx];
            int dx = ToScreenDx(p.X - dragStart.X), dy = ToScreenDx(p.Y - dragStart.Y);
            int snap = Math.Max(4, ToScreenDx(10));
            if (resizing)
            {
                int w = Math.Max(160, dragOrig.Width + dx);
                int h = Aspect > 0 ? (int)Math.Round(w / Aspect) : Math.Max(90, dragOrig.Height + dy);
                // snap right/bottom edges
                int right = SnapEdge(dragOrig.X + w, true, dragIdx, snap);
                if (right != dragOrig.X + w) { w = right - dragOrig.X; if (Aspect > 0) h = (int)Math.Round(w / Aspect); }
                if (Aspect <= 0) { int bottom = SnapEdge(dragOrig.Y + h, false, dragIdx, snap); h = bottom - dragOrig.Y; }
                t.Px = new Rectangle(dragOrig.X, dragOrig.Y, w, h);
            }
            else
            {
                int x = dragOrig.X + dx, y = dragOrig.Y + dy;
                int sx = SnapEdge(x, true, dragIdx, snap); if (sx != x) x = sx;
                else { int sr = SnapEdge(x + dragOrig.Width, true, dragIdx, snap); if (sr != x + dragOrig.Width) x = sr - dragOrig.Width; }
                int sy = SnapEdge(y, false, dragIdx, snap); if (sy != y) y = sy;
                else { int sb = SnapEdge(y + dragOrig.Height, false, dragIdx, snap); if (sb != y + dragOrig.Height) y = sb - dragOrig.Height; }
                t.Px = new Rectangle(x, y, dragOrig.Width, dragOrig.Height);
            }
        }

        // Nearest monitor edge or other-slot edge within `snap` px along one axis.
        int SnapEdge(int v, bool horizontal, int self, int snap)
        {
            int best = v, bestD = snap + 1;
            foreach (Rectangle m in Monitors)
            {
                int[] edges = horizontal ? new int[] { m.Left, m.Right } : new int[] { m.Top, m.Bottom };
                foreach (int ed in edges) { int d = Math.Abs(ed - v); if (d < bestD) { bestD = d; best = ed; } }
            }
            for (int i = 0; i < Tiles.Count; i++)
            {
                if (i == self) continue;
                Rectangle r = Tiles[i].Px;
                int[] edges = horizontal ? new int[] { r.Left, r.Right } : new int[] { r.Top, r.Bottom };
                foreach (int ed in edges) { int d = Math.Abs(ed - v); if (d < bestD) { bestD = d; best = ed; } }
            }
            return best;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            if (dragIdx >= 0 && dragging)
            {
                if (EditMode) { if (SlotsChanged != null) SlotsChanged(this, EventArgs.Empty); }
                else
                {
                    int target = HitTile(e.Location);
                    if (target >= 0 && target != dragIdx && SwapRequested != null) SwapRequested(dragIdx, target);
                }
            }
            dragIdx = -1; hoverIdx = -1; dragging = false; resizing = false;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (EditMode) return;
            int i = HitTile(e.Location);
            if (i >= 0 && !Tiles[i].Empty && !Tiles[i].IsMain && MainRequested != null) MainRequested(i);
        }
    }

    // -------------------------------------------------------------------------
    // Owner-drawn TabControl so the tab strip isn't a white bar in dark mode
    // -------------------------------------------------------------------------
    class DarkTabs : TabControl
    {
        public DarkTabs()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(90, 30);
            Appearance = TabAppearance.Normal;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg = Color.FromArgb(24, 26, 34), card = Color.FromArgb(33, 36, 47), line = Color.FromArgb(58, 62, 78);
            Color text = Color.FromArgb(226, 229, 238), muted = Color.FromArgb(140, 146, 165), accent = Color.FromArgb(235, 185, 60);
            e.Graphics.Clear(bg);
            if (TabCount == 0) return;
            Rectangle page = GetTabRect(0); 
            int top = page.Bottom;
            using (var b = new SolidBrush(card)) e.Graphics.FillRectangle(b, new Rectangle(0, top, Width, Height - top));
            using (var p = new Pen(line)) e.Graphics.DrawRectangle(p, new Rectangle(0, top, Width - 1, Height - top - 1));
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle r = GetTabRect(i);
                bool sel = i == SelectedIndex;
                using (var b = new SolidBrush(sel ? card : bg)) e.Graphics.FillRectangle(b, r);
                if (sel) using (var b = new SolidBrush(accent)) e.Graphics.FillRectangle(b, new Rectangle(r.X, r.Y, r.Width, 2));
                TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, r, sel ? text : muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Main window
    // -------------------------------------------------------------------------
    class MainForm : Form
    {
        static readonly string[] LayoutKeys = { "l_left", "l_right", "mains_top", "mains_left", "main_left", "main_right", "main_top", "main_bottom", "grid", "custom" };
        static readonly string[] LayoutNames = {
            "L-shape, main top-left (all 16:9)",
            "L-shape, main top-right (all 16:9)",
            "Mains across the top, alts along the bottom",
            "Mains on the left, alts stacked right",
            "Main left, alts stacked right",
            "Main right, alts stacked left",
            "Main top, alts along bottom",
            "Main bottom, alts along top",
            "Even grid",
            "Custom (draw your own in the preview)" };

        // How many of the leading slots count as "mains" (big, gold, promotion targets).
        int MainCount
        {
            get
            {
                bool multi = cfg.Layout == "mains_top" || cfg.Layout == "mains_left" || cfg.Layout == "custom";
                return multi ? Math.Max(1, Math.Min(cfg.Mains, Math.Max(1, order.Count))) : 1;
            }
        }
        static readonly string[] AspectKeys = { "16:9", "16:10", "21:9", "4:3", "free" };
        static readonly string[] AspectNames = { "16:9 (widescreen)", "16:10", "21:9 (ultrawide)", "4:3", "Free (fill the slot)" };

        Config cfg;
        Screen[] monitors;
        List<IntPtr> order = new List<IntPtr>();                      // slot 0 = main
        Dictionary<IntPtr, WowWindow> known = new Dictionary<IntPtr, WowWindow>();
        Dictionary<int, string> hotkeyActions = new Dictionary<int, string>();
        bool loading = true;
        int labelCounter = 0;
        bool raidMode = false;
        IntPtr raidMain = IntPtr.Zero;
        Button raidButton;
        NotifyIcon tray;
        CheckBox startupBox, startMinBox, swapFocusBox;
        ComboBox swapModBox, altMonitorBox;
        LayoutPreview preview;
        Label previewHintLabel;
        Button updateButton;
        CheckBox updateCheckBox;
        bool updating;                           // restarting into a new version: skip the border cleanup
        Size normalSize;                         // the window's designed size
        Rectangle goodBounds = Rectangle.Empty;  // last bounds seen while properly open (not minimized/stub)

        bool LooksCollapsed { get { return Width < 400 || Height < 200 || Left <= -30000 || Top <= -30000; } }

        void RememberGoodBounds()
        {
            if (Visible && WindowState == FormWindowState.Normal && !LooksCollapsed) goodBounds = Bounds;
        }

        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); RememberGoodBounds(); }

        void RestoreProperBounds(string why)
        {
            Log.Write("Restoring window bounds (" + why + "): was " + Bounds + ", good=" + goodBounds + ", design=" + normalSize);
            if (goodBounds.Width > 400 && goodBounds.Height > 200) Bounds = goodBounds;
            else
            {
                Size = normalSize.Width > 400 ? normalSize : new Size(S(900), S(820));
                EnsureOnTilingMonitor();
            }
        }
        Button editButton, addSlotButton;
        NumericUpDown reserveTop, reserveBottom, reserveLeft, reserveRight;

        NumericUpDown ReserveBox(int value)
        {
            var n = new NumericUpDown { Minimum = 0, Maximum = 2000, Increment = 10, Width = 58, Value = value, Margin = new Padding(0, 0, 6, 0) };
            n.ValueChanged += delegate { SettingChanged(); };
            return n;
        }

        static Label Tiny(string t)
        {
            return new Label { Text = t, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 5, 3, 0), Font = new Font("Segoe UI", 8F) };
        }
        KeyRecorder slotRecorder;
        Dictionary<string, KeyRecorder> recorders = new Dictionary<string, KeyRecorder>();
        static readonly string[] SwapModKeys = { "shift", "ctrl", "alt", "ctrl+shift", "none" };
        static readonly string[] SwapModNames = { "Shift + click", "Ctrl + click", "Alt + click", "Ctrl+Shift + click", "Plain click (any focus)" };
        Native.WinEventProc fgProc;            // kept alive for the hook
        IntPtr fgHook = IntPtr.Zero;
        IntPtr pendingSwap = IntPtr.Zero;
        DateTime lastTiled = DateTime.MinValue;
        Timer swapTimer, topmostTimer;
        bool startHidden;
        bool reallyClose;

        ComboBox layoutBox, aspectBox, monitorBox, methodBox;
        TrackBar mainSize;
        Label mainSizeLabel;
        NumericUpDown gapBox, mainsBox;
        CheckBox autoBox, borderBox, focusBox, topBox, taskbarBox, fillBox, raidMinBox;
        TableLayoutPanel clientTable;
        Label statusLabel;
        Timer pollTimer;

        public MainForm(bool minimized)
        {
            cfg = Config.Load();
            monitors = SortedScreens();
            startHidden = minimized || cfg.StartMinimized;
            BuildUi();
            BuildTray();
            InstallFocusHook();
            EnsureOnTilingMonitor();
            loading = false;
            if (cfg.CheckUpdates && Updater.Configured)
            {
                var t = new Timer { Interval = 6000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); CheckForUpdates(false); };
                t.Start();
                var periodic = new Timer { Interval = 6 * 60 * 60 * 1000 };
                periodic.Tick += delegate { if (cfg.CheckUpdates && pendingUpdate == null) CheckForUpdates(false); };
                periodic.Start();
            }
            TopMost = cfg.AlwaysOnTop;


            pollTimer = new Timer();
            pollTimer.Interval = 1500;
            pollTimer.Tick += delegate { Poll(); };
            pollTimer.Start();

            RefreshClients(cfg.AutoApply);
        }

        static Screen[] SortedScreens()
        {
            var list = new List<Screen>(Screen.AllScreens);
            list.Sort(delegate(Screen a, Screen b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                int c = a.Bounds.X.CompareTo(b.Bounds.X);
                return c != 0 ? c : a.Bounds.Y.CompareTo(b.Bounds.Y);
            });
            return list.ToArray();
        }

        // ---------------- UI ----------------

        public const string Version = "1.3.3";
        static readonly Color Accent = Color.FromArgb(235, 185, 60);     // gold (main slot)
        static readonly Color AccentDark = Color.FromArgb(150, 110, 25);
        static readonly Color Bg = Color.FromArgb(24, 26, 34);           // window background
        static readonly Color Card = Color.FromArgb(33, 36, 47);         // panels
        static readonly Color Field = Color.FromArgb(44, 48, 62);        // inputs, chips
        static readonly Color Line = Color.FromArgb(58, 62, 78);
        static readonly Color Fg = Color.FromArgb(226, 229, 238);
        static readonly Color Muted = Color.FromArgb(140, 146, 165);
        static readonly Color Danger = Color.FromArgb(235, 90, 90);
        static readonly Color Good = Color.FromArgb(90, 200, 130);
        static readonly Color Header = Color.FromArgb(17, 19, 26);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static void DarkTitleBar(IntPtr h)
        {
            int on = 1;
            try { if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch (Exception) { }
        }

        Label clientHeader;
        Panel statusStrip;
        Label statusDot;
        float dpiScale = 1f;
        int S(int px) { return (int)Math.Round(px * dpiScale); }

        void BuildUi()
        {
            using (Graphics g = CreateGraphics()) dpiScale = g.DpiY / 96f;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "WoW Window Tiler " + Version;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            ClientSize = new Size(880, 760);

            // ----- header -----
            var header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Header };
            var title = new Label { Text = "WoW Window Tiler", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13F),
                AutoSize = true, Location = new Point(16, 9), BackColor = Color.Transparent };
            var sub = new Label { Text = "Tiles your clients, keeps them 16:9, one key to swap the main.  Works with Retail, Classic, WoW Forever and PTR clients.", ForeColor = Color.FromArgb(170, 176, 195),
                AutoSize = true, Location = new Point(17, 32), BackColor = Color.Transparent };
            raidButton = Flat("Raid mode", Accent, Header, delegate { ToggleRaidMode(); });
            raidButton.AutoSize = false; raidButton.Size = new Size(130, 32); raidButton.Margin = new Padding(0);
            raidButton.Font = new Font("Segoe UI Semibold", 9F);
            var headerRight = new Panel { Dock = DockStyle.Right, Width = 150, BackColor = Color.Transparent };
            raidButton.Location = new Point(4, 11);
            headerRight.Controls.Add(raidButton);
            header.Controls.Add(title); header.Controls.Add(sub); header.Controls.Add(headerRight);
            Controls.Add(header);

            // ----- status strip (bottom) -----
            statusStrip = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Header };
            statusDot = new Label { Text = "\u25CF", AutoSize = true, Location = new Point(10, 6), ForeColor = Muted, Font = new Font("Segoe UI", 10F) };
            statusLabel = new Label { AutoSize = false, Location = new Point(28, 0), Height = 30, Width = 720, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg, Text = "Starting...", AutoEllipsis = true };
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            statusStrip.Controls.Add(statusDot); statusStrip.Controls.Add(statusLabel);
            Controls.Add(statusStrip);

            // ----- body: two columns -----
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12, 10, 12, 8) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(body);
            body.BringToFront();

            // --- left: settings card with tabs ---
            var tabs = new DarkTabs { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            var tabLayout = new TabPage("Layout") { BackColor = Card, ForeColor = Fg, Padding = new Padding(10) };
            var tabBehavior = new TabPage("Behavior") { BackColor = Card, ForeColor = Fg, Padding = new Padding(10) };
            var tabHotkeys = new TabPage("Hotkeys") { BackColor = Card, ForeColor = Fg, Padding = new Padding(10) };
            tabs.TabPages.Add(tabLayout); tabs.TabPages.Add(tabBehavior); tabs.TabPages.Add(tabHotkeys);
            body.Controls.Add(tabs, 0, 0);

            // Layout tab
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;

            layoutBox = Combo(LayoutNames, Math.Max(0, Array.IndexOf(LayoutKeys, cfg.Layout)));
            AddRow(grid, ref row, "Layout", layoutBox);

            aspectBox = Combo(AspectNames, Math.Max(0, Array.IndexOf(AspectKeys, cfg.Aspect)));
            AddRow(grid, ref row, "Aspect", aspectBox);

            var monNames = new List<string>();
            for (int i = 0; i < monitors.Length; i++)
                monNames.Add(string.Format("{0}: {1}x{2}{3}", i + 1, monitors[i].Bounds.Width, monitors[i].Bounds.Height, monitors[i].Primary ? "  (primary)" : ""));
            monitorBox = Combo(monNames.ToArray(), Math.Max(0, Math.Min(cfg.Monitor, monitors.Length - 1)));
            AddRow(grid, ref row, "Monitor", monitorBox);

            var altNames = new List<string>(); altNames.Add("Same monitor (use layout)");
            for (int i = 0; i < monitors.Length; i++) altNames.Add("Monitor " + (i + 1) + "  (" + monitors[i].Bounds.Width + "x" + monitors[i].Bounds.Height + ")");
            altMonitorBox = Combo(altNames.ToArray(), cfg.AltMonitor < 0 ? 0 : Math.Min(cfg.AltMonitor + 1, monitors.Length));
            AddRow(grid, ref row, "Alts on", altMonitorBox);

            var sizeRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            mainSize = new TrackBar { Minimum = 40, Maximum = 90, TickFrequency = 10, Value = cfg.MainPercent, AutoSize = false, Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0) };
            mainSizeLabel = new Label { Text = cfg.MainPercent + "%", AutoSize = false, Width = 42, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, ForeColor = Muted };
            mainSize.ValueChanged += delegate { mainSizeLabel.Text = mainSize.Value + "%"; };
            mainSize.MouseUp += delegate { SettingChanged(); };
            mainSize.KeyUp += delegate { SettingChanged(); };
            sizeRow.Controls.Add(mainSize, 0, 0); sizeRow.Controls.Add(mainSizeLabel, 1, 0);
            AddRow(grid, ref row, "Main size", sizeRow);

            var gapRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            gapBox = new NumericUpDown { Minimum = 0, Maximum = 60, Width = 56, Value = cfg.Gap };
            gapBox.ValueChanged += delegate { SettingChanged(); };
            gapRow.Controls.Add(gapBox);
            gapRow.Controls.Add(new Label { Text = "px between windows", AutoSize = true, ForeColor = Muted, Margin = new Padding(6, 5, 0, 0) });
            AddRow(grid, ref row, "Gap", gapRow);

            var mainsRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            mainsBox = new NumericUpDown { Minimum = 1, Maximum = 4, Width = 56, Value = Math.Max(1, Math.Min(4, cfg.Mains)) };
            mainsBox.ValueChanged += delegate { SettingChanged(); };
            mainsRow.Controls.Add(mainsBox);
            mainsRow.Controls.Add(new Label { Text = "big slots (for the 'Mains ...' and Custom layouts)", AutoSize = true, ForeColor = Muted, Margin = new Padding(6, 5, 0, 0) });
            AddRow(grid, ref row, "Mains", mainsRow);

            var reserve = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            reserveTop = ReserveBox(cfg.ReserveTop); reserveBottom = ReserveBox(cfg.ReserveBottom);
            reserveLeft = ReserveBox(cfg.ReserveLeft); reserveRight = ReserveBox(cfg.ReserveRight);
            reserve.Controls.Add(Tiny("T")); reserve.Controls.Add(reserveTop);
            reserve.Controls.Add(Tiny("B")); reserve.Controls.Add(reserveBottom);
            reserve.Controls.Add(Tiny("L")); reserve.Controls.Add(reserveLeft);
            reserve.Controls.Add(Tiny("R")); reserve.Controls.Add(reserveRight);
            new ToolTip().SetToolTip(reserve, "Pixels to keep free at each screen edge (for a stream overlay, dock, second taskbar...).");
            AddRow(grid, ref row, "Keep free", reserve);

            fillBox = Check("Fill the screen (main absorbs leftover space)", cfg.FillScreen);
            taskbarBox = Check("Cover the taskbar", cfg.CoverTaskbar);
            borderBox = Check("Borderless clients (no title bars)", cfg.Borderless);
            var layoutChecks = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0), WrapContents = false };
            layoutChecks.Controls.Add(fillBox); layoutChecks.Controls.Add(taskbarBox); layoutChecks.Controls.Add(borderBox);

            var layoutHelp = Note("L-shape keeps every window exactly 16:9 (main + alts). Fill-the-screen uses every pixel but lets the main go off-ratio.");
            tabLayout.Controls.Add(layoutHelp); tabLayout.Controls.Add(layoutChecks); tabLayout.Controls.Add(grid);
            layoutHelp.Dock = DockStyle.Bottom;

            // Behavior tab
            autoBox = Check("Auto-tile when clients open or close", cfg.AutoApply);
            focusBox = Check("Focus the client swapped to main", cfg.FocusOnSwap);
            topBox = Check("Keep this window on top", cfg.AlwaysOnTop);
            raidMinBox = Check("Raid mode minimizes alts (otherwise they stay behind)", cfg.RaidMinimizeAlts);
            startupBox = Check("Start with Windows", Startup.IsEnabled);
            startMinBox = Check("Start minimized to the tray", cfg.StartMinimized);
            swapFocusBox = Check("Shift+click into an alt to make it the main", cfg.SwapOnFocus);
            var behavior = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            behavior.Controls.Add(autoBox); behavior.Controls.Add(focusBox); behavior.Controls.Add(topBox); behavior.Controls.Add(raidMinBox);
            behavior.Controls.Add(swapFocusBox); behavior.Controls.Add(startupBox); behavior.Controls.Add(startMinBox);

            var methodGrid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0) };
            methodGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            methodGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int mrow = 0;
            methodBox = Combo(new string[] { "Fake a mouse drag (default)", "Two-step resize", "Minimize + restore", "Plain resize", "Real drag (moves mouse briefly)" },
                Math.Max(0, Math.Min(cfg.ResizeMethod, 4)));
            AddRow(methodGrid, ref mrow, "Resize via", methodBox);

            // Updates
            var updRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 12, 0, 0) };
            updateButton = Flat("Check for updates", Field, Fg, delegate { CheckForUpdates(true); });
            updRow.Controls.Add(updateButton);
            updRow.Controls.Add(new Label { Text = "version " + Version, AutoSize = true, ForeColor = Muted, Margin = new Padding(4, 6, 0, 0) });
            updateCheckBox = Check("Check for updates when the tiler starts", cfg.CheckUpdates);
            updateCheckBox.Margin = new Padding(0, 6, 0, 0);
            var updPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0) };
            updPanel.Controls.Add(updRow); updPanel.Controls.Add(updateCheckBox);

            var behaviorHelp = Note("Clients must be in plain Windowed mode (not Windowed (Fullscreen)), or they render at a fixed resolution and look blurry when enlarged. 'Fix display mode' writes that into each client's Config.wtf.");
            tabBehavior.Controls.Add(behaviorHelp); tabBehavior.Controls.Add(updPanel); tabBehavior.Controls.Add(methodGrid); tabBehavior.Controls.Add(behavior);
            behaviorHelp.Dock = DockStyle.Bottom;

            // Hotkeys tab
            var hk = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            hk.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            hk.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int hrow = 0;
            AddRow(hk, ref hrow, "Next -> main", Recorder("next"));
            AddRow(hk, ref hrow, "Prev -> main", Recorder("prev"));
            AddRow(hk, ref hrow, "Re-tile", Recorder("retile"));
            AddRow(hk, ref hrow, "Raid mode", Recorder("raid"));
            slotRecorder = new KeyRecorder { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            string slotPrefix = Get(cfg.Hotkeys, "slot1");
            int cut = slotPrefix.LastIndexOf('+'); if (cut > 0) slotPrefix = slotPrefix.Substring(0, cut);
            slotRecorder.Set(slotPrefix + "+1");
            slotRecorder.BindingChanged += delegate
            {
                // Whatever key they press, keep the modifiers and bind 1..9 with them.
                string b = slotRecorder.Binding;
                int c2 = b.LastIndexOf('+');
                string mods = c2 > 0 ? b.Substring(0, c2) : "";
                for (int n = 1; n <= 9; n++) cfg.Hotkeys["slot" + n] = mods.Length == 0 ? n.ToString() : mods + "+" + n;
                slotRecorder.Set(mods.Length == 0 ? "1" : mods + "+1");
                cfg.Save(); RegisterHotkeys();
            };
            AddRow(hk, ref hrow, "Slot 1-9 -> main", slotRecorder);

            swapModBox = Combo(SwapModNames, Math.Max(0, Array.IndexOf(SwapModKeys, cfg.SwapModifier)));
            AddRow(hk, ref hrow, "Click-swap key", swapModBox);

            var resetBtn = Flat("Reset to defaults", Field, Fg, delegate
            {
                var d = new Config();
                foreach (string n in Config.HotkeyNames) cfg.Hotkeys[n] = d.Hotkeys[n];
                cfg.SwapModifier = d.SwapModifier;
                foreach (KeyValuePair<string, KeyRecorder> kv in recorders) kv.Value.Set(cfg.Hotkeys[kv.Key]);
                slotRecorder.Set("ctrl+alt+1");
                swapModBox.SelectedIndex = Math.Max(0, Array.IndexOf(SwapModKeys, cfg.SwapModifier));
                cfg.Save(); RegisterHotkeys();
            });
            resetBtn.Margin = new Padding(0, 10, 0, 0);
            var resetRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
            resetRow.Controls.Add(resetBtn);
            var hkHelp = Note("Click a box and press the combination you want (Backspace clears it). Hotkeys are global and apply right away. Click-swap: hold that key and click into an alt to make it the main.");
            tabHotkeys.Controls.Add(hkHelp); tabHotkeys.Controls.Add(resetRow); tabHotkeys.Controls.Add(hk);
            hkHelp.Dock = DockStyle.Bottom;

            // --- right: clients card ---
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0) };
            card.Paint += delegate(object sndr, PaintEventArgs pe)
            {
                var r = card.ClientRectangle; r.Width--; r.Height--;
                using (var pen = new Pen(Line)) pe.Graphics.DrawRectangle(pen, r);
            };
            body.Controls.Add(card, 1, 0);

            clientHeader = new Label { Text = "Clients", Font = new Font("Segoe UI Semibold", 10.5F), AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0), ForeColor = Fg };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 6) };
            tools.Controls.Add(Flat("Apply layout", Accent, Header, delegate { ApplyLayout(false); }));
            tools.Controls.Add(Flat("Next \u25B6", Field, Fg, delegate { Rotate(1); }));
            tools.Controls.Add(Flat("\u25C0 Prev", Field, Fg, delegate { Rotate(-1); }));
            tools.Controls.Add(Flat("Refresh", Field, Fg, delegate { RefreshClients(false); }));
            tools.Controls.Add(Flat("Fix display mode", Color.FromArgb(70, 56, 24), Accent, delegate { FixDisplayMode(); }));

            clientTable = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 5, Margin = new Padding(0) };
            clientTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            clientTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            clientTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            clientTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            clientTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));

            preview = new LayoutPreview { Dock = DockStyle.Top, Height = S(230), Margin = new Padding(0), Font = new Font("Segoe UI", 8F) };
            preview.SwapRequested += delegate(int a, int b) { SwapSlots(a, b); };
            preview.MainRequested += delegate(int a) { if (a < order.Count) MakeMain(order[a]); };
            preview.DeleteRequested += delegate(int a) { DeleteCustomSlot(a); };
            preview.SlotsChanged += delegate { CustomSlotsEdited(); };
            var previewHint = new Label { Dock = DockStyle.Top, AutoSize = false, Height = S(20), ForeColor = Muted, Font = new Font("Segoe UI", 8F),
                Text = "Drag a client onto another to swap. Double-click = make main.", TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            previewHintLabel = previewHint;

            var editRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 4) };
            editButton = Flat("Customize layout", Field, Fg, delegate { ToggleEdit(); });
            addSlotButton = Flat("+ Add slot", Field, Fg, delegate { AddCustomSlot(); });
            addSlotButton.Visible = false;
            editRow.Controls.Add(editButton); editRow.Controls.Add(addSlotButton);

            card.Controls.Add(clientTable); card.Controls.Add(editRow); card.Controls.Add(previewHint); card.Controls.Add(preview); card.Controls.Add(tools); card.Controls.Add(clientHeader);

            TopMost = cfg.AlwaysOnTop;
            ResumeLayout(false);
            PerformLayout();
            Darken(this);
            normalSize = Size;
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { tray.Icon = SystemIcons.Application; }
            tray.Text = "WoW Window Tiler";
            tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { Log.Write("Tray: Open clicked."); BeginInvoke((MethodInvoker)delegate { ShowFromTray(); }); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Apply layout", null, delegate { ApplyLayout(false); });
            menu.Items.Add("Next -> main", null, delegate { Rotate(1); });
            menu.Items.Add("Prev -> main", null, delegate { Rotate(-1); });
            menu.Items.Add("Raid mode", null, delegate { ToggleRaidMode(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { reallyClose = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Log.Write("Tray: double-click."); BeginInvoke((MethodInvoker)delegate { ShowFromTray(); }); };
        }

        // ---------------- preview & custom layout editor ----------------

        void UpdatePreview()
        {
            if (preview == null) return;
            preview.Monitors = InvolvedMonitors();
            preview.Canvas = CanvasRect();
            preview.Aspect = ParseAspect(cfg.Aspect);
            preview.MainCount = MainCount;
            preview.EditMode = preview.EditMode && cfg.Layout == "custom";
            var tiles = new List<LayoutPreview.Tile>();
            List<Rectangle> slots = CurrentSlots(Math.Max(order.Count, preview.EditMode ? 0 : (order.Count == 0 && cfg.Layout != "custom" ? 5 : order.Count)));
            for (int i = 0; i < slots.Count; i++)
            {
                var t = new LayoutPreview.Tile { Px = slots[i] };
                if (i < order.Count) { t.Hwnd = order[i]; t.Label = known[order[i]].Label; t.IsMain = i < MainCount; }
                else t.Empty = true;
                tiles.Add(t);
            }
            preview.Tiles = tiles;
            preview.Invalidate();
        }

        void SwapSlots(int a, int b)
        {
            if (a < 0 || b < 0 || a >= order.Count || b >= order.Count) return;
            IntPtr t = order[a]; order[a] = order[b]; order[b] = t;
            ApplyLayout(a == 0 || b == 0);
        }

        void ToggleEdit()
        {
            if (!preview.EditMode)
            {
                if (cfg.Layout != "custom")
                {
                    // Seed the custom slots from whatever the current layout would do.
                    List<Rectangle> seed = CurrentSlots(Math.Max(order.Count, 5));
                    Rectangle c = CanvasRect();
                    cfg.CustomSlots.Clear();
                    foreach (Rectangle r in seed) cfg.CustomSlots.Add(ToFrac(r, c));
                    cfg.Layout = "custom";
                    loading = true; layoutBox.SelectedIndex = Array.IndexOf(LayoutKeys, "custom"); loading = false;
                    cfg.Save();
                }
                preview.EditMode = true;
                editButton.Text = "Done editing";
                addSlotButton.Visible = true;
                previewHintLabel.Text = "Drag a slot to move it, drag its gold corner to resize (16:9 kept). Right-click a slot to delete it. Snaps to edges.";
                SetStatus("Editing the custom layout. Clients follow as you drag.");
            }
            else
            {
                preview.EditMode = false;
                editButton.Text = "Customize layout";
                addSlotButton.Visible = false;
                previewHintLabel.Text = "Drag a client onto another to swap. Double-click = make main.";
                cfg.Save();
                if (cfg.AutoApply && !raidMode && order.Count > 0) ApplyLayout(false);
            }
            UpdatePreview();
        }

        static RectangleF ToFrac(Rectangle r, Rectangle c)
        {
            return new RectangleF((r.X - c.X) / (float)c.Width, (r.Y - c.Y) / (float)c.Height, r.Width / (float)c.Width, r.Height / (float)c.Height);
        }

        void CustomSlotsEdited()
        {
            Rectangle c = CanvasRect();
            cfg.CustomSlots.Clear();
            foreach (LayoutPreview.Tile t in preview.Tiles) cfg.CustomSlots.Add(ToFrac(t.Px, c));
            cfg.Save();
            if (cfg.AutoApply && !raidMode && order.Count > 0) ApplyLayout(false); else UpdatePreview();
        }

        void AddCustomSlot()
        {
            if (cfg.Layout != "custom") return;
            Rectangle c = CanvasRect();
            double aspect = ParseAspect(cfg.Aspect); if (aspect <= 0) aspect = 16.0 / 9.0;
            int w = c.Width / 4, h = (int)Math.Round(w / aspect);
            int n = cfg.CustomSlots.Count;
            cfg.CustomSlots.Add(ToFrac(new Rectangle(c.X + c.Width / 2 - w / 2 + n * 20, c.Y + c.Height / 2 - h / 2 + n * 20, w, h), c));
            cfg.Save();
            UpdatePreview();
        }

        void DeleteCustomSlot(int i)
        {
            if (cfg.Layout != "custom" || i < 0 || i >= cfg.CustomSlots.Count) return;
            cfg.CustomSlots.RemoveAt(i);
            cfg.Save();
            if (cfg.AutoApply && !raidMode && order.Count > 0 && cfg.CustomSlots.Count > 0) ApplyLayout(false); else UpdatePreview();
        }

        // ---------------- updates ----------------

        Updater.Info pendingUpdate;
        bool offeringUpdate;

        // manual = the user clicked the button, so always say something; otherwise stay quiet unless there's an update.
        void CheckForUpdates(bool manual)
        {
            if (!Updater.Configured)
            {
                if (manual) MessageBox.Show(this, "This build has no update source configured.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            updateButton.Enabled = false;
            if (manual) SetStatus("Checking for updates...");
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object sndr, System.ComponentModel.DoWorkEventArgs e) { e.Result = Updater.Check(); };
            worker.RunWorkerCompleted += delegate(object sndr, System.ComponentModel.RunWorkerCompletedEventArgs e)
            {
                updateButton.Enabled = true;
                var info = e.Result as Updater.Info;
                if (info == null || info.Error.Length > 0)
                {
                    if (manual) SetStatus("Update check failed: " + (info != null ? info.Error : "unknown error"));
                    return;
                }
                if (info.Latest <= Updater.Current)
                {
                    if (manual) SetStatus("You're on the latest version (" + Version + ").");
                    return;
                }
                pendingUpdate = info;
                SetStatus("Version " + info.Latest + " is available.");
                if (raidMode && !manual)
                {
                    // Don't interrupt a raid; the prompt comes up when raid mode ends.
                    tray.ShowBalloonTip(8000, "WoW Window Tiler " + info.Latest + " available", "You'll be asked to update when you leave raid mode.", ToolTipIcon.Info);
                    return;
                }
                if (!Visible) ShowFromTray();
                OfferUpdate(info);
            };
            worker.RunWorkerAsync();
        }

        void OnUpdateBalloonClicked(object sender, EventArgs e)
        {
            tray.BalloonTipClicked -= OnUpdateBalloonClicked;
            if (pendingUpdate != null) { ShowFromTray(); OfferUpdate(pendingUpdate); }
        }

        void OfferUpdate(Updater.Info info)
        {
            if (offeringUpdate) return;
            offeringUpdate = true;
            bool wasTop = TopMost;
            TopMost = true;
            DialogResult r;
            try
            {
                r = MessageBox.Show(this,
                    "Version " + info.Latest + " is available (you have " + Version + ").\n\nDownload and restart into it now?\nYour settings and layout are kept.",
                    "WoW Window Tiler update", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            }
            finally { TopMost = wasTop || cfg.AlwaysOnTop; offeringUpdate = false; }
            if (r != DialogResult.Yes) { pendingUpdate = null; SetStatus("Update to " + info.Latest + " skipped. Use 'Check for updates' on the Behavior tab any time."); return; }
            updateButton.Enabled = false;
            SetStatus("Downloading version " + info.Latest + "...");
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object sndr, System.ComponentModel.DoWorkEventArgs e) { e.Result = Updater.Download(info.DownloadUrl); };
            worker.RunWorkerCompleted += delegate(object sndr, System.ComponentModel.RunWorkerCompletedEventArgs e)
            {
                updateButton.Enabled = true;
                string err = e.Result as string;
                if (!string.IsNullOrEmpty(err)) { SetStatus("Download failed: " + err); return; }
                RestartIntoUpdate();
            };
            worker.RunWorkerAsync();
        }

        // Start a fresh copy that waits for us to exit, then quit. Its Main() swaps WowTiler.new.exe in.
        void RestartIntoUpdate()
        {
            try
            {
                string argsOut = "--wait-for " + Process.GetCurrentProcess().Id + (startHidden ? " --minimized" : "");
                Process.Start(Application.ExecutablePath, argsOut);
            }
            catch (Exception ex) { SetStatus("Could not restart: " + ex.Message + "  (restart the tiler by hand to finish the update)"); return; }
            updating = true;
            reallyClose = true;
            Close();
        }

        // ---------------- swap on focus ----------------
        // Windows tells us whenever the foreground window changes. If it's one of our
        // alts and Shift is held (and we didn't just re-tile), it becomes the main.

        void InstallFocusHook()
        {
            fgProc = OnForegroundChanged;
            fgHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, fgProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            swapTimer = new Timer { Interval = 180 };
            swapTimer.Tick += delegate
            {
                swapTimer.Stop();
                IntPtr h = pendingSwap; pendingSwap = IntPtr.Zero;
                if (h == IntPtr.Zero || Native.GetForegroundWindow() != h) return;   // they moved on; ignore
                if (order.Count > 1 && order.IndexOf(h) >= MainCount) MakeMain(h);
            };
        }

        bool SwapModifierHeld()
        {
            string m = cfg.SwapModifier ?? "shift";
            if (m == "none") return true;
            foreach (string part in m.Split('+'))
            {
                int vk = part == "shift" ? 0x10 : part == "ctrl" ? 0x11 : part == "alt" ? 0x12 : 0;
                if (vk != 0 && (Native.GetAsyncKeyState(vk) & 0x8000) == 0) return false;
            }
            return true;
        }

        void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (!cfg.SwapOnFocus || raidMode || loading) return;
            if (hwnd == IntPtr.Zero || !order.Contains(hwnd) || order.IndexOf(hwnd) < MainCount) return;
            if (!SwapModifierHeld()) return;
            if ((DateTime.Now - lastTiled).TotalMilliseconds < 800) return;         // our own re-tile shuffles focus
            pendingSwap = hwnd;
            swapTimer.Stop(); swapTimer.Start();
        }

        bool allowVisible;

        // First Show() is swallowed when starting hidden; the handle is still created so
        // hotkeys and the tray work. Nothing here recreates the window handle.
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        // Put the window on the tiling monitor, centered, and fully inside its working area.
        // (WinForms' CenterScreen means the monitor the MOUSE is on, which may be a dark or
        // off-to-the-side screen, and that made the tray's Open look like it did nothing.)
        void EnsureOnTilingMonitor()
        {
            Screen s = monitors[Math.Max(0, Math.Min(cfg.Monitor, monitors.Length - 1))];
            Rectangle wa = s.WorkingArea;
            int x = wa.X + (wa.Width - Width) / 2, y = wa.Y + (wa.Height - Height) / 2;
            x = Math.Max(wa.X, Math.Min(x, wa.Right - Width)); y = Math.Max(wa.Y, Math.Min(y, wa.Bottom - Height));
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
        }

        void ShowFromTray()
        {
            try
            {
                Log.Write(string.Format("ShowFromTray: visible={0} state={1} handle={2} bounds={3}", Visible, WindowState, IsHandleCreated, Bounds));
                allowVisible = true;
                if (!IsHandleCreated) CreateHandle();
                // If it went to the tray by being minimized, Windows may still hold the minimized
                // placement (an off-screen 199x34 stub) even though WinForms says "Normal".
                // Never trust the flag: fix the bounds before and after showing.
                if (LooksCollapsed) RestoreProperBounds("before show");
                Show();
                if (Native.IsIconic(Handle)) { Log.Write("ShowFromTray: window was iconic, restoring"); Native.ShowWindow(Handle, Native.SW_RESTORE); }
                WindowState = FormWindowState.Normal;
                if (LooksCollapsed) RestoreProperBounds("after show");
                Opacity = 1.0;
                if (goodBounds.IsEmpty) EnsureOnTilingMonitor();
                ShowFromTrayFront();
                RememberGoodBounds();
                Log.Write(string.Format("ShowFromTray done: visible={0} state={1} bounds={2}", Visible, WindowState, Bounds));
            }
            catch (Exception ex) { Log.Write("ShowFromTray failed: " + ex); }
        }

        void ShowFromTrayFront()
        {
            // Windows may deny us the foreground (we weren't the last process clicked), which
            // would leave the window behind the full-screen WoW clients. Briefly going topmost
            // puts it in front regardless; then we drop back to the user's setting.
            TopMost = true;
            BringToFront();
            Activate();
            Native.ForceForeground(Handle);
            if (topmostTimer == null)
            {
                topmostTimer = new Timer { Interval = 400 };
                topmostTimer.Tick += delegate { topmostTimer.Stop(); TopMost = cfg.AlwaysOnTop; };
            }
            topmostTimer.Stop(); topmostTimer.Start();
        }

        void HideToTray()
        {
            Hide();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (IsHandleCreated && Native.IsIconic(Handle)) { Native.ShowWindow(Handle, Native.SW_RESTORE); Hide(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RememberGoodBounds();
            if (WindowState == FormWindowState.Minimized && Visible) BeginInvoke((MethodInvoker)delegate { HideToTray(); });
        }

        // Apply the dark palette to any control the constructors above didn't cover.
        static void Darken(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ComboBox)
                {
                    var cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Field; cb.ForeColor = Fg;
                    cb.DrawMode = DrawMode.OwnerDrawFixed; cb.DrawItem += DrawComboItem;
                }
                else if (c is NumericUpDown) { c.BackColor = Field; c.ForeColor = Fg; }
                else if (c is TextBox) { c.BackColor = Field; c.ForeColor = Fg; }
                else if (c is TrackBar) { c.BackColor = Card; }
                else if (c is CheckBox) { c.ForeColor = Fg; ((CheckBox)c).FlatStyle = FlatStyle.Standard; }
                else if (c is TableLayoutPanel || c is FlowLayoutPanel) { c.BackColor = Color.Transparent; }
                Darken(c);
            }
        }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Field : Field)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Color.FromArgb(60, 66, 86))) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }



        static string Get(Dictionary<string, string> d, string k) { return d.ContainsKey(k) ? d[k] : "?"; }

        KeyRecorder Recorder(string name)
        {
            var r = new KeyRecorder { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            r.Set(Get(cfg.Hotkeys, name));
            r.BindingChanged += delegate { cfg.Hotkeys[name] = r.Binding; cfg.Save(); RegisterHotkeys(); };
            recorders[name] = r;
            return r;
        }

        ComboBox Combo(string[] items, int selected)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Min(selected, items.Length - 1);
            c.SelectedIndexChanged += delegate { SettingChanged(); };
            return c;
        }

        static void AddRow(TableLayoutPanel grid, ref int row, string label, Control ctl)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 7, 6, 3), ForeColor = Muted }, 0, row);
            if (ctl.Dock != DockStyle.Fill) ctl.Anchor = AnchorStyles.Left;
            grid.Controls.Add(ctl, 1, row);
            row++;
        }

        CheckBox Check(string text, bool value)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Checked = value, Margin = new Padding(0, 3, 0, 3) };
            c.CheckedChanged += delegate { SettingChanged(); };
            return c;
        }

        static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = false, Height = 58, ForeColor = Muted, Dock = DockStyle.Bottom, Font = new Font("Segoe UI", 8.25F) };
        }

        static Label Key(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Consolas", 9.5F), BackColor = Field,
                ForeColor = Accent, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 3, 0, 3) };
        }

        static Button Flat(string text, Color back, Color fore, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore,
                Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.1f);
            b.EnabledChanged += delegate { b.ForeColor = b.Enabled ? fore : Muted; };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 6, 6, 3) };
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(4, 0, 4, 0) };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        void SetStatus(string text)
        {
            statusLabel.Text = text;
            string t = text.ToLowerInvariant();
            statusDot.ForeColor = (t.Contains("blurry") || t.Contains("blocking") || t.Contains("not registered") || t.Contains("no wow clients")) ? Danger
                               : (t.StartsWith("tiled") || t.StartsWith("raid mode")) ? Good : Muted;
        }

        void RebuildClientList()
        {
            clientTable.SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in clientTable.Controls) old.Add(c);
            clientTable.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            clientTable.RowStyles.Clear();

            int bad = 0;
            foreach (WowWindow kw in known.Values) if (kw.NeedsFix) bad++;
            int ignoredCount = 0;
            foreach (WowWindow kw in known.Values) if (kw.Ignored) ignoredCount++;
            clientHeader.Text = known.Count == 0 ? "Clients"
                : string.Format("Clients  \u00B7  {0} running{1}{2}", known.Count,
                    ignoredCount > 0 ? "  \u00B7  " + ignoredCount + " not tiled" : "",
                    bad > 0 ? "  \u00B7  " + bad + " need display-mode fix" : "");
            clientHeader.ForeColor = bad > 0 ? Danger : Fg;

            if (known.Count == 0)
            {
                var none = new Label { Text = "No WoW clients found.\n\nLog in on one or more clients and they will appear here. Clients must be in Windowed mode.",
                    AutoSize = false, Dock = DockStyle.Fill, Height = S(90), ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
                clientTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                clientTable.Controls.Add(none, 0, 0); clientTable.SetColumnSpan(none, 5);
                clientTable.ResumeLayout(true);
                return;
            }

            clientTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            clientTable.Controls.Add(Hdr("SLOT"), 0, 0);
            clientTable.Controls.Add(Hdr("LABEL"), 1, 0);
            clientTable.Controls.Add(Hdr("STATUS"), 2, 0);
            clientTable.Controls.Add(Hdr(""), 3, 0);
            clientTable.Controls.Add(Hdr("TILE"), 4, 0);

            var rows = new List<WowWindow>();
            foreach (IntPtr h in order) rows.Add(known[h]);
            foreach (WowWindow kw in known.Values) if (kw.Ignored) rows.Add(kw);

            for (int i = 0; i < rows.Count; i++)
            {
                WowWindow w = rows[i];
                bool isMain = !w.Ignored && order.IndexOf(w.Hwnd) >= 0 && order.IndexOf(w.Hwnd) < MainCount;
                int slotNo = order.IndexOf(w.Hwnd) + 1;
                int r = i + 1;
                clientTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var slot = new Label { Text = w.Ignored ? "\u2014" : isMain ? "MAIN" : slotNo.ToString(), AutoSize = false, Width = S(52), Height = S(26),
                    TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, S(4), S(8), S(4)),
                    BackColor = isMain ? Accent : w.Ignored ? Card : Field,
                    ForeColor = isMain ? Header : w.Ignored ? Muted : Fg,
                    Font = new Font("Segoe UI Semibold", isMain ? 8.5F : 9.5F) };
                clientTable.Controls.Add(slot, 0, r);

                var box = new TextBox { Dock = DockStyle.Fill, Text = w.Label, Tag = w, Margin = new Padding(0, S(5), S(8), S(5)), BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Field, ForeColor = w.Ignored ? Muted : Fg };
                box.TextChanged += delegate(object sndr, EventArgs e) { ((WowWindow)((TextBox)sndr).Tag).Label = ((TextBox)sndr).Text; SaveLabels(); };
                clientTable.Controls.Add(box, 1, r);

                string st; Color sc;
                if (w.Fullscreen) { st = "Fullscreen (exclusive)"; sc = Danger; }
                else if (w.Maximized == 1) { st = "Windowed (Fullscreen)"; sc = Danger; }
                else if (w.Maximized == 0) { st = "Windowed  \u2713"; sc = Good; }
                else { st = "PID " + w.Pid; sc = Muted; }
                if (w.Ignored) { st = "not tiled"; sc = Muted; }
                var status = new Label { Text = st, AutoSize = false, Width = S(144), Height = S(26), TextAlign = ContentAlignment.MiddleLeft, ForeColor = sc, Margin = new Padding(0, S(4), S(4), S(4)) };
                new ToolTip().SetToolTip(status, "PID " + w.Pid + (w.ExeDir.Length > 0 ? "\n" + w.ExeDir : ""));
                clientTable.Controls.Add(status, 2, r);

                var btn = Flat(isMain ? "current" : "Make main", isMain ? Card : Field, isMain ? Muted : Fg, null);
                btn.Tag = w.Hwnd; btn.Enabled = !isMain; btn.Margin = new Padding(0, S(3), 0, S(3)); btn.AutoSize = false; btn.Size = new Size(S(86), S(28));
                btn.Click += delegate(object sndr, EventArgs e) { MakeMain((IntPtr)((Button)sndr).Tag); };
                clientTable.Controls.Add(btn, 3, r);

                var tile = new CheckBox { Checked = !w.Ignored, Tag = w.Hwnd, AutoSize = true, Margin = new Padding(S(14), S(8), 0, 0) };
                new ToolTip().SetToolTip(tile, "Untick to leave this client where it is (it won't be moved or resized).");
                tile.CheckedChanged += delegate(object sndr, EventArgs e) { var c = (CheckBox)sndr; SetIgnored((IntPtr)c.Tag, !c.Checked); };
                clientTable.Controls.Add(tile, 4, r);
            }
            clientTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            clientTable.ResumeLayout(true);
            UpdatePreview();
        }

        static Label Hdr(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2) };
        }

        void SaveLabels()
        {
            if (loading) return;
            foreach (WowWindow w in known.Values)
            {
                while (cfg.Labels.Count <= w.LoginIndex) cfg.Labels.Add("");
                cfg.Labels[w.LoginIndex] = w.Label.Replace("|", "/");
            }
            cfg.Save();
        }

        // ---------------- settings ----------------

        void SettingChanged()
        {
            if (loading) return;
            cfg.Layout = LayoutKeys[Math.Max(0, layoutBox.SelectedIndex)];
            cfg.Aspect = AspectKeys[Math.Max(0, aspectBox.SelectedIndex)];
            cfg.Monitor = Math.Max(0, monitorBox.SelectedIndex);
            cfg.MainPercent = mainSize.Value;
            cfg.Gap = (int)gapBox.Value;
            cfg.Mains = (int)mainsBox.Value;
            cfg.AutoApply = autoBox.Checked;
            cfg.Borderless = borderBox.Checked;
            cfg.FocusOnSwap = focusBox.Checked;
            cfg.AlwaysOnTop = topBox.Checked;
            cfg.CoverTaskbar = taskbarBox.Checked;
            cfg.FillScreen = fillBox.Checked;
            cfg.ResizeMethod = Math.Max(0, methodBox.SelectedIndex);
            cfg.RaidMinimizeAlts = raidMinBox.Checked;
            cfg.StartMinimized = startMinBox.Checked;
            cfg.SwapOnFocus = swapFocusBox.Checked;
            cfg.SwapModifier = SwapModKeys[Math.Max(0, swapModBox.SelectedIndex)];
            cfg.CheckUpdates = updateCheckBox.Checked;
            cfg.AltMonitor = altMonitorBox.SelectedIndex - 1;
            cfg.ReserveTop = (int)reserveTop.Value; cfg.ReserveBottom = (int)reserveBottom.Value;
            cfg.ReserveLeft = (int)reserveLeft.Value; cfg.ReserveRight = (int)reserveRight.Value;
            if (startupBox.Checked != Startup.IsEnabled)
            {
                bool ok = startupBox.Checked ? Startup.Enable() : Startup.Disable();
                if (!ok) { SetStatus("Could not change the Windows startup shortcut."); startupBox.Checked = Startup.IsEnabled; }
                else SetStatus(startupBox.Checked ? "Will start with Windows (shortcut in your Startup folder)." : "Removed from Windows startup.");
            }
            TopMost = cfg.AlwaysOnTop;
            cfg.Save();
            if (preview != null && preview.EditMode && cfg.Layout != "custom") ToggleEdit();   // switched away from custom: leave the editor
            if (cfg.AutoApply && !raidMode) ApplyLayout(false);
            UpdatePreview();
        }

        Rectangle MonitorArea(int index)
        {
            Screen s = monitors[Math.Max(0, Math.Min(index, monitors.Length - 1))];
            Rectangle r = cfg.CoverTaskbar ? s.Bounds : s.WorkingArea;
            r = new Rectangle(r.X + cfg.ReserveLeft, r.Y + cfg.ReserveTop,
                              Math.Max(200, r.Width - cfg.ReserveLeft - cfg.ReserveRight),
                              Math.Max(200, r.Height - cfg.ReserveTop - cfg.ReserveBottom));
            return r;
        }

        Rectangle Area() { return MonitorArea(cfg.Monitor); }

        Rectangle CanvasRect()
        {
            return AltsOnOtherMonitor ? Rectangle.Union(Area(), MonitorArea(cfg.AltMonitor)) : Area();
        }

        List<Rectangle> InvolvedMonitors()
        {
            var l = new List<Rectangle>(); l.Add(Area());
            if (AltsOnOtherMonitor) l.Add(MonitorArea(cfg.AltMonitor));
            return l;
        }

        static int LShapeN(int k, double mainFrac)
        {
            int nMin = (k + 2) / 2;
            int nn = Math.Max(nMin, (int)Math.Round(1.0 / Math.Max(0.05, 1.0 - mainFrac)));
            return Math.Max(2, nn);
        }

        // Every slot of the current layout (including empty ones) for n clients.
        List<Rectangle> CurrentSlots(int n)
        {
            double aspect = ParseAspect(cfg.Aspect);
            if (cfg.Layout == "custom")
            {
                Rectangle c = CanvasRect();
                var l = new List<Rectangle>();
                foreach (RectangleF f in cfg.CustomSlots)
                    l.Add(new Rectangle(c.X + (int)Math.Round(f.X * c.Width), c.Y + (int)Math.Round(f.Y * c.Height),
                                        Math.Max(100, (int)Math.Round(f.Width * c.Width)), Math.Max(60, (int)Math.Round(f.Height * c.Height))));
                return l;
            }
            if (n <= 0) return new List<Rectangle>();
            if (AltsOnOtherMonitor)
            {
                Rectangle mainArea = Area(), altArea = MonitorArea(cfg.AltMonitor);
                var l = new List<Rectangle>();
                l.Add(cfg.FillScreen ? mainArea : FitCell(mainArea, aspect));
                l.AddRange(PackCells(new Rectangle(altArea.X + cfg.Gap, altArea.Y + cfg.Gap, altArea.Width - 2 * cfg.Gap, altArea.Height - 2 * cfg.Gap), n - 1, aspect, cfg.Gap, 0, 0));
                return l;
            }
            int total = n;
            if ((cfg.Layout == "l_left" || cfg.Layout == "l_right") && n >= 2) total = 2 * LShapeN(n - 1, cfg.MainPercent / 100.0);
            return ComputeRects(total, Area(), cfg.Layout, cfg.MainPercent / 100.0, cfg.Gap, aspect, cfg.FillScreen, cfg.Mains);
        }

        bool AltsOnOtherMonitor
        {
            get { return cfg.AltMonitor >= 0 && cfg.AltMonitor != cfg.Monitor && cfg.AltMonitor < monitors.Length; }
        }

        // ---------------- layout math ----------------

        // "16:9" -> 1.777..., "free" -> 0 (no constraint)
        static double ParseAspect(string text)
        {
            if (text == null) return 0;
            int colon = text.IndexOf(':');
            double a, b;
            if (colon > 0
                && double.TryParse(text.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                && double.TryParse(text.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                && a > 0 && b > 0)
                return a / b;
            return 0;
        }

        // Largest window inside `cell` whose *client area* has the given aspect,
        // given the window's frame overhead. Centered in the cell.
        static Rectangle FitAspect(Rectangle cell, double aspect, int frameW, int frameH)
        {
            if (aspect <= 0) return cell;
            int availW = cell.Width - frameW, availH = cell.Height - frameH;
            if (availW <= 0 || availH <= 0) return cell;
            int cw = availW;
            int ch = (int)Math.Round(cw / aspect);
            if (ch > availH) { ch = availH; cw = (int)Math.Round(ch * aspect); }
            int w = cw + frameW, h = ch + frameH;
            return new Rectangle(cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h);
        }

        // Fit a rectangle of the given aspect inside `cell`, centered. aspect <= 0 = fill.
        static Rectangle FitCell(Rectangle cell, double aspect)
        {
            if (aspect <= 0 || cell.Width <= 0 || cell.Height <= 0) return cell;
            int w = cell.Width, h = (int)Math.Round(w / aspect);
            if (h > cell.Height) { h = cell.Height; w = (int)Math.Round(h * aspect); }
            return new Rectangle(cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h);
        }

        // Best width for k equal aspect-ratio cells packed in a regionW x regionH box,
        // trying every column count. widthLimited says whether the region's width
        // (rather than its height) is what stops the cells from being bigger.
        static double BestCellWidth(int regionW, int regionH, int k, double aspect, int gap, out int bestCols, out bool widthLimited)
        {
            bestCols = 1; widthLimited = true;
            double best = -1;
            if (k <= 0 || regionW <= 0 || regionH <= 0) return 0;
            for (int c = 1; c <= k; c++)
            {
                int r = (int)Math.Ceiling(k / (double)c);
                double byWidth = (regionW - gap * (c - 1)) / (double)c;
                double byHeight = (regionH - gap * (r - 1)) / (double)r * aspect;
                double w = Math.Min(byWidth, byHeight);
                if (w > best) { best = w; bestCols = c; widthLimited = byWidth <= byHeight; }
            }
            return Math.Max(0, best);
        }

        // Pack k aspect-ratio cells into `region` as a grid. anchorX/anchorY: -1 = start, 0 = center, 1 = end.
        static List<Rectangle> PackCells(Rectangle region, int k, double aspect, int gap, int anchorX, int anchorY)
        {
            var rects = new List<Rectangle>();
            if (k <= 0 || region.Width <= 0 || region.Height <= 0) return rects;
            int cols, rows, cw, ch;
            if (aspect <= 0)
            {
                cols = (int)Math.Ceiling(Math.Sqrt(k));
                rows = (int)Math.Ceiling(k / (double)cols);
                cw = (region.Width - gap * (cols - 1)) / cols;
                ch = (region.Height - gap * (rows - 1)) / rows;
            }
            else
            {
                bool wl;
                double w = BestCellWidth(region.Width, region.Height, k, aspect, gap, out cols, out wl);
                rows = (int)Math.Ceiling(k / (double)cols);
                cw = (int)w; ch = (int)(w / aspect);
            }
            int totalW = cols * cw + gap * (cols - 1), totalH = rows * ch + gap * (rows - 1);
            int ox = region.X + (anchorX < 0 ? 0 : anchorX > 0 ? region.Width - totalW : (region.Width - totalW) / 2);
            int oy = region.Y + (anchorY < 0 ? 0 : anchorY > 0 ? region.Height - totalH : (region.Height - totalH) / 2);
            for (int i = 0; i < k; i++)
                rects.Add(new Rectangle(ox + (i % cols) * (cw + gap), oy + (i / cols) * (ch + gap), cw, ch));
            return rects;
        }

        // Slot 0 = main. The main starts at `mainFrac` of the screen and grows as long
        // as that costs the alts nothing (they stay the same size until the side
        // region becomes the limiting factor), so screens fill edge to edge.
        // Lay out `m` mains and `k` alts, all at `aspect`. Candidates:
        //   (a) two bands: mains on top (or left), alts flush against them in the rest;
        //   (b) a mains grid with empty cells, alts packed into the hole.
        // Every mains grid x alts grid at every main size is tried; the arrangement leaving
        // the least empty screen wins. Alts are never larger than mains, and never tiny
        // (at least a third of a main's width and an eighth of the screen).
        static List<Rectangle> SolveBands(Rectangle area, int m, int k, double aspect, int gap, bool top)
        {
            int W = area.Width, H = area.Height;
            double minAltAbs = W / 8.0;
            double bestWaste = double.MaxValue;
            int bestRows = int.MaxValue;
            List<Rectangle> best = null;
            const int steps = 160;
            double samePenalty = 0.08 * W * H;      // "mains" the same size as alts defeats the point; count it as 8% waste

            // (a) bands
            for (int cm = 1; cm <= m; cm++)
            {
                int rm = (m + cm - 1) / cm;
                double mwMax = Math.Min((W - gap * (cm - 1)) / (double)cm, (H - gap * (rm - 1)) / (double)rm * aspect);
                if (mwMax <= 0) continue;
                for (int t = steps; t >= steps / 4; t--)
                {
                    double mw = mwMax * t / steps, mh = mw / aspect;
                    double bandMain = top ? rm * mh + gap * (rm - 1) : cm * mw + gap * (cm - 1);
                    double restSpan = (top ? H : W) - bandMain - gap;
                    if (restSpan <= 0) continue;
                    for (int ca = 1; ca <= k; ca++)
                    {
                        int ra = (k + ca - 1) / ca;
                        double aw;
                        if (top) aw = Math.Min((W - gap * (ca - 1)) / (double)ca, (restSpan - gap * (ra - 1)) / ra * aspect);
                        else     aw = Math.Min((restSpan - gap * (ca - 1)) / (double)ca, (H - gap * (ra - 1)) / ra * aspect);
                        if (aw <= 0 || aw > mw + 0.5 || aw < 0.33 * mw || aw < minAltAbs) continue;
                        double waste = (double)W * H - m * mw * mh - k * aw * (aw / aspect);
                        if (aw >= 0.9 * mw) waste += samePenalty;
                        if (waste < bestWaste - 0.5 || (Math.Abs(waste - bestWaste) <= 0.5 && ra < bestRows))
                        {
                            bestWaste = waste; bestRows = ra;
                            best = BandRects(area, m, k, cm, ca, (int)mw, (int)(mw / aspect), (int)aw, (int)(aw / aspect), gap, top);
                        }
                    }
                }
            }

            // (b) grid with a hole for the alts
            for (int cm = 1; cm <= m + 1; cm++)
            {
                int rm = (m + cm - 1) / cm;
                int holes = cm * rm - m;
                if (holes <= 0) continue;
                double mw = Math.Min((W - gap * (cm - 1)) / (double)cm, (H - gap * (rm - 1)) / (double)rm * aspect), mh = mw / aspect;
                double holeW = holes * mw + gap * (holes - 1), holeH = mh;
                for (int ca = 1; ca <= k; ca++)
                {
                    int ra = (k + ca - 1) / ca;
                    double aw = Math.Min((holeW - gap * (ca - 1)) / ca, (holeH - gap * (ra - 1)) / ra * aspect);
                    if (aw <= 0 || aw < 0.33 * mw || aw < minAltAbs) continue;
                    double waste = (double)W * H - m * mw * mh - k * aw * (aw / aspect);
                    if (aw >= 0.9 * mw) waste += samePenalty;
                    if (waste < bestWaste - 0.5 || (Math.Abs(waste - bestWaste) <= 0.5 && ra < bestRows))
                    {
                        bestWaste = waste; bestRows = ra;
                        int MW = (int)mw, MH = (int)mh, AW = (int)aw, AH = (int)(aw / aspect);
                        int gridW = cm * MW + gap * (cm - 1), gridH = rm * MH + gap * (rm - 1);
                        int gx = area.X + (W - gridW) / 2, gy = area.Y + (H - gridH) / 2;
                        var r = new List<Rectangle>();
                        for (int i = 0; i < m; i++) r.Add(new Rectangle(gx + (i % cm) * (MW + gap), gy + (i / cm) * (MH + gap), MW, MH));
                        int hx = gx + (cm - holes) * (MW + gap), hy = gy + (rm - 1) * (MH + gap);
                        int altsW = ca * AW + gap * (ca - 1), altsH = ra * AH + gap * (ra - 1);
                        int ax = hx + ((int)holeW - altsW) / 2, ay = hy;
                        for (int i = 0; i < k; i++) r.Add(new Rectangle(ax + (i % ca) * (AW + gap), ay + (i / ca) * (AH + gap), AW, AH));
                        best = r;
                    }
                }
            }

            if (best == null) best = PackCells(area, m + k, aspect, gap, 0, 0);
            return best;
        }

        static List<Rectangle> BandRects(Rectangle area, int m, int k, int cm, int ca, int MW, int MH, int AW, int AH, int gap, bool top)
        {
            int W = area.Width, H = area.Height;
            int rm = (m + cm - 1) / cm, ra = (k + ca - 1) / ca;
            int mainsW = cm * MW + gap * (cm - 1), mainsH = rm * MH + gap * (rm - 1);
            int altsW = ca * AW + gap * (ca - 1), altsH = ra * AH + gap * (ra - 1);
            int mx, my, ax, ay;
            if (top) { mx = area.X + (W - mainsW) / 2; my = area.Y; ax = area.X + (W - altsW) / 2; ay = area.Y + mainsH + gap; }
            else     { mx = area.X; my = area.Y + (H - mainsH) / 2; ax = area.X + mainsW + gap; ay = area.Y + (H - altsH) / 2; }
            var rects = new List<Rectangle>();
            for (int i = 0; i < m; i++) rects.Add(new Rectangle(mx + (i % cm) * (MW + gap), my + (i / cm) * (MH + gap), MW, MH));
            for (int i = 0; i < k; i++) rects.Add(new Rectangle(ax + (i % ca) * (AW + gap), ay + (i / ca) * (AH + gap), AW, AH));
            return rects;
        }

        static List<Rectangle> ComputeRects(int n, Rectangle area, string layout, double mainFrac, int gap, double aspect, bool fill, int mains)
        {
            var rects = new List<Rectangle>();
            if (n == 0) return rects;
            area = new Rectangle(area.X + gap, area.Y + gap, area.Width - 2 * gap, area.Height - 2 * gap);

            if (n == 1) { rects.Add(fill ? area : FitCell(area, aspect)); return rects; }
            if (layout == "grid") return PackCells(area, n, fill ? 0 : aspect, gap, 0, 0);

            if (layout == "mains_top" || layout == "mains_left")
            {
                int m = Math.Max(1, Math.Min(mains, n)), rest = n - m;
                if (rest == 0) return PackCells(area, m, fill ? 0 : aspect, gap, 0, 0);
                if (m == 1 && !fill && aspect > 0)
                    return ComputeRects(n, new Rectangle(area.X - gap, area.Y - gap, area.Width + 2 * gap, area.Height + 2 * gap), "l_left", mainFrac, gap, aspect, fill, 1);
                if (fill || aspect <= 0)
                {
                    // Free aspect: simple proportional split, both bands filled.
                    Rectangle big, small;
                    if (layout == "mains_top")
                    {
                        int bh = (int)(area.Height * mainFrac);
                        big = new Rectangle(area.X, area.Y, area.Width, bh - gap / 2);
                        small = new Rectangle(area.X, area.Y + bh + gap / 2, area.Width, area.Height - bh - gap / 2);
                    }
                    else
                    {
                        int bw = (int)(area.Width * mainFrac);
                        big = new Rectangle(area.X, area.Y, bw - gap / 2, area.Height);
                        small = new Rectangle(area.X + bw + gap / 2, area.Y, area.Width - bw - gap / 2, area.Height);
                    }
                    rects.AddRange(PackCells(big, m, 0, gap, 0, 0));
                    rects.AddRange(PackCells(small, rest, 0, gap, 0, 0));
                    return rects;
                }
                return SolveBands(area, m, rest, aspect, gap, layout == "mains_top");
            }

            int k = n - 1, cols; bool wl;
            const int step = 4;

            if (layout == "l_left" || layout == "l_right")
            {
                // Tile the screen with one big tile and small tiles of the SAME aspect.
                // With n small tiles down the side, the main is (n-1)/n of the width and
                // there are 2n-1 small slots (a column of n and a row of n-1 under the main).
                // The slider chooses n; n is raised as needed so every alt gets a slot.
                double a = aspect > 0 ? aspect : area.Width / (double)area.Height;
                int nMin = (k + 2) / 2;                                           // ceil((k+1)/2)
                int nn = Math.Max(nMin, (int)Math.Round(1.0 / Math.Max(0.05, 1.0 - mainFrac)));
                nn = Math.Max(2, nn);
                int tileH = (area.Height - gap * (nn - 1)) / nn;
                int tileW = (int)Math.Round(tileH * a);
                if (nn * tileW + gap * (nn - 1) > area.Width)
                {
                    tileW = (area.Width - gap * (nn - 1)) / nn;
                    tileH = (int)Math.Round(tileW / a);
                }
                int mainW = (nn - 1) * tileW + gap * (nn - 2);
                int mainH = (nn - 1) * tileH + gap * (nn - 2);
                bool mainLeft = layout == "l_left";
                int mainX = mainLeft ? area.X : area.X + tileW + gap;
                int colX = mainLeft ? area.X + mainW + gap : area.X;
                rects.Add(new Rectangle(mainX, area.Y, mainW, mainH));

                // Slot order: down the whole side column (corner included), then along the
                // bottom row starting beside the column and moving away from it.
                var slots = new List<Rectangle>();
                for (int i = 0; i < nn; i++)
                    slots.Add(new Rectangle(colX, area.Y + i * (tileH + gap), tileW, tileH));
                int rowY = area.Y + mainH + gap;
                for (int i = 0; i < nn - 1; i++)
                {
                    int j = mainLeft ? nn - 2 - i : i;     // main-left: column is on the right, so walk right-to-left
                    slots.Add(new Rectangle(mainX + j * (tileW + gap), rowY, tileW, tileH));
                }
                for (int i = 0; i < k && i < slots.Count; i++) rects.Add(slots[i]);
                return rects;
            }

            if (layout == "main_left" || layout == "main_right")
            {
                int mw = Math.Max(1, (int)(area.Width * mainFrac));
                if (fill)
                {
                    // Gap-free arrangements: c columns of aspect-correct alts filling the full
                    // height; the main takes everything left. The slider picks the arrangement
                    // whose main share is closest to it (with 4 alts on a 16:9 screen there is
                    // only one such arrangement, so the slider has nothing to choose).
                    int bestC = 0, bestCellH = 0, bestCellW = 0, bestColW = 0; double bestScore = double.MaxValue;
                    if (aspect > 0)
                        for (int c = 1; c <= k; c++)
                        {
                            int r = (k + c - 1) / c;
                            int cellH = (area.Height - gap * (r - 1)) / r;
                            int cellW = (int)Math.Round(cellH * aspect);
                            int colW = c * cellW + gap * (c - 1);
                            int mainWc = area.Width - colW - gap;
                            if (mainWc < area.Width * 0.25) continue;
                            double score = Math.Abs(mainWc / (double)area.Width - mainFrac);
                            if (score < bestScore) { bestScore = score; bestC = c; bestCellH = cellH; bestCellW = cellW; bestColW = colW; }
                        }
                    int colWidth = bestC > 0 ? bestColW : area.Width - mw - gap;
                    int mainW = area.Width - colWidth - gap;
                    Rectangle fMain, fSide; int fAnchor;
                    if (layout == "main_left")
                    {
                        fMain = new Rectangle(area.X, area.Y, mainW, area.Height);
                        fSide = new Rectangle(area.X + mainW + gap, area.Y, colWidth, area.Height);
                        fAnchor = -1;
                    }
                    else
                    {
                        fSide = new Rectangle(area.X, area.Y, colWidth, area.Height);
                        fMain = new Rectangle(area.X + colWidth + gap, area.Y, mainW, area.Height);
                        fAnchor = 1;
                    }
                    rects.Add(fMain);
                    if (bestC > 0)
                    {
                        int rowsF = (k + bestC - 1) / bestC;
                        int ox = fAnchor < 0 ? fSide.X : fSide.Right - bestColW;
                        for (int i = 0; i < k; i++)
                            rects.Add(new Rectangle(ox + (i % bestC) * (bestCellW + gap), fSide.Y + (i / bestC) * (bestCellH + gap), bestCellW, bestCellH));
                    }
                    else if (aspect > 0) rects.AddRange(PackCells(fSide, k, aspect, gap, fAnchor, -1));
                    else
                    {
                        int cellH = (area.Height - gap * (k - 1)) / k;
                        for (int i = 0; i < k; i++) rects.Add(new Rectangle(fSide.X, fSide.Y + i * (cellH + gap), colWidth, cellH));
                    }
                    return rects;
                }
                if (aspect > 0)
                {
                    double altAtSlider = BestCellWidth(area.Width - mw - gap, area.Height, k, aspect, gap, out cols, out wl);
                    while (mw + step < area.Width - gap)
                    {
                        if (mw / aspect >= area.Height) break;                       // main already full height
                        double alt = BestCellWidth(area.Width - (mw + step) - gap, area.Height, k, aspect, gap, out cols, out wl);
                        if (alt < altAtSlider - 0.5) break;                           // alts would start shrinking
                        mw += step;
                    }
                    mw = Math.Min(mw, (int)Math.Round(area.Height * aspect));        // never wider than full-height needs
                }
                int sw = area.Width - mw - gap;
                Rectangle mainCell, side;
                int anchorX;
                if (layout == "main_left")
                {
                    mainCell = new Rectangle(area.X, area.Y, mw, area.Height);
                    side = new Rectangle(area.X + mw + gap, area.Y, sw, area.Height);
                    anchorX = -1;   // alts hug the main
                }
                else
                {
                    side = new Rectangle(area.X, area.Y, sw, area.Height);
                    mainCell = new Rectangle(area.X + sw + gap, area.Y, mw, area.Height);
                    anchorX = 1;
                }
                rects.Add(FitCell(mainCell, aspect));
                rects.AddRange(PackCells(side, k, aspect, gap, anchorX, 0));
            }
            else // main_top (alts along bottom) / main_bottom (alts along top)
            {
                int mh = Math.Max(1, (int)(area.Height * mainFrac));
                if (fill)
                {
                    int bestR = 0, bestCellH = 0, bestCellW = 0, bestStripH = 0; double bestScore = double.MaxValue;
                    if (aspect > 0)
                        for (int r = 1; r <= k; r++)
                        {
                            int c = (k + r - 1) / r;
                            int cellW = (area.Width - gap * (c - 1)) / c;
                            int cellH = (int)Math.Round(cellW / aspect);
                            int stripH = r * cellH + gap * (r - 1);
                            int mainHc = area.Height - stripH - gap;
                            if (mainHc < area.Height * 0.25) continue;
                            double score = Math.Abs(mainHc / (double)area.Height - mainFrac);
                            if (score < bestScore) { bestScore = score; bestR = r; bestCellH = cellH; bestCellW = cellW; bestStripH = stripH; }
                        }
                    int stripHeight = bestR > 0 ? bestStripH : area.Height - mh - gap;
                    int mainH = area.Height - stripHeight - gap;
                    Rectangle fMain, fStrip; int fAnchor;
                    if (layout == "main_top")
                    {
                        fMain = new Rectangle(area.X, area.Y, area.Width, mainH);
                        fStrip = new Rectangle(area.X, area.Y + mainH + gap, area.Width, stripHeight);
                        fAnchor = -1;
                    }
                    else
                    {
                        fStrip = new Rectangle(area.X, area.Y, area.Width, stripHeight);
                        fMain = new Rectangle(area.X, area.Y + stripHeight + gap, area.Width, mainH);
                        fAnchor = 1;
                    }
                    rects.Add(fMain);
                    if (bestR > 0)
                    {
                        int colsF = (k + bestR - 1) / bestR;
                        int oy = fAnchor < 0 ? fStrip.Y : fStrip.Bottom - bestStripH;
                        for (int i = 0; i < k; i++)
                            rects.Add(new Rectangle(fStrip.X + (i % colsF) * (bestCellW + gap), oy + (i / colsF) * (bestCellH + gap), bestCellW, bestCellH));
                    }
                    else if (aspect > 0) rects.AddRange(PackCells(fStrip, k, aspect, gap, 0, fAnchor));
                    else
                    {
                        int cellW = (area.Width - gap * (k - 1)) / k;
                        for (int i = 0; i < k; i++) rects.Add(new Rectangle(fStrip.X + i * (cellW + gap), fStrip.Y, cellW, stripHeight));
                    }
                    return rects;
                }
                if (aspect > 0)
                {
                    double altAtSlider = BestCellWidth(area.Width, area.Height - mh - gap, k, aspect, gap, out cols, out wl);
                    while (mh + step < area.Height - gap)
                    {
                        if (mh * aspect >= area.Width) break;                        // main already full width
                        double alt = BestCellWidth(area.Width, area.Height - (mh + step) - gap, k, aspect, gap, out cols, out wl);
                        if (alt < altAtSlider - 0.5) break;
                        mh += step;
                    }
                    mh = Math.Min(mh, (int)Math.Round(area.Width / aspect));
                }
                int sh = area.Height - mh - gap;
                Rectangle mainCell, strip;
                int anchorY;
                if (layout == "main_top")
                {
                    mainCell = new Rectangle(area.X, area.Y, area.Width, mh);
                    strip = new Rectangle(area.X, area.Y + mh + gap, area.Width, sh);
                    anchorY = -1;
                }
                else
                {
                    strip = new Rectangle(area.X, area.Y, area.Width, sh);
                    mainCell = new Rectangle(area.X, area.Y + sh + gap, area.Width, mh);
                    anchorY = 1;
                }
                rects.Add(FitCell(mainCell, aspect));
                rects.AddRange(PackCells(strip, k, aspect, gap, 0, anchorY));
            }
            return rects;
        }

        // ---------------- core actions ----------------

        // Re-scan for WoW windows: keep existing slot order, append newcomers.
        void RefreshClients(bool applyNow)
        {
            List<WowWindow> found = Native.FindWowWindows();
            found.Sort(delegate(WowWindow a, WowWindow b) { return a.Pid.CompareTo(b.Pid); });

            var alive = new HashSet<IntPtr>();
            foreach (WowWindow w in found)
            {
                alive.Add(w.Hwnd);
                if (!known.ContainsKey(w.Hwnd))
                {
                    labelCounter++;
                    w.Label = (labelCounter - 1 < cfg.Labels.Count && cfg.Labels[labelCounter - 1].Trim().Length > 0)
                        ? cfg.Labels[labelCounter - 1] : "Client " + labelCounter;
                    w.LoginIndex = labelCounter - 1;
                    w.ReadConfig();
                    known[w.Hwnd] = w;
                }
            }
            foreach (IntPtr h in new List<IntPtr>(known.Keys))
                if (!alive.Contains(h)) known.Remove(h);

            order.RemoveAll(delegate(IntPtr h) { return !alive.Contains(h); });
            foreach (WowWindow w in found)
                if (!order.Contains(w.Hwnd) && !known[w.Hwnd].Ignored) order.Add(w.Hwnd);

            RebuildClientList();
            if (applyNow && order.Count > 0) ApplyLayout(false);
            else if (order.Count == 0) SetStatus("No WoW clients found.");
            else SetStatus(order.Count + " client(s) found.");
        }

        void ApplyLayout(bool focus)
        {
            if (order.Count == 0) { SetStatus("Nothing to tile."); return; }
            if (raidMode) LeaveRaidModeState();
            double aspect = ParseAspect(cfg.Aspect);
            List<Rectangle> rects = CurrentSlots(order.Count);
            if (rects.Count == 0) { SetStatus("The custom layout has no slots yet. Click 'Customize layout' and add some."); UpdatePreview(); return; }
            while (rects.Count < order.Count)        // more clients than slots: pile the extras on the last slot
            {
                Rectangle last = rects[rects.Count - 1];
                rects.Add(new Rectangle(last.X + 30, last.Y + 30, last.Width, last.Height));
            }
            // Activate the new main BEFORE resizing it: WoW appears to re-apply its stored
            // resolution on activation, which would undo a resize done just before.
            if (focus && cfg.FocusOnSwap)
            {
                IntPtr f = lastPromoted != IntPtr.Zero && order.Contains(lastPromoted) ? lastPromoted : order[0];
                Native.ForceForeground(f);
                System.Threading.Thread.Sleep(150);
            }
            var placed = new Dictionary<IntPtr, Rectangle>();
            for (int i = 0; i < order.Count; i++)
            {
                WowWindow w = known[order[i]];
                if (!Native.IsWindow(w.Hwnd)) continue;
                if (Native.IsIconic(w.Hwnd) || Native.IsZoomed(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
                if (w.SetBorderless(cfg.Borderless)) Native.CommitFrame(w.Hwnd);
                Rectangle target = rects[i];
                bool exactCells = cfg.FillScreen || cfg.Layout == "l_left" || cfg.Layout == "l_right" || cfg.Layout == "custom" || cfg.Layout == "mains_top" || cfg.Layout == "mains_left" || AltsOnOtherMonitor;
                if (!exactCells)
                {
                    int frameW, frameH;
                    Native.FrameSize(w.Hwnd, out frameW, out frameH);
                    target = FitAspect(rects[i], aspect, frameW, frameH);
                }
                Native.Place(w.Hwnd, target, cfg.ResizeMethod);
                placed[w.Hwnd] = target;
            }
            // Second pass: a window that crossed to a monitor with different DPI scaling gets
            // re-adjusted by Windows right after our move and can end up off target. Check
            // where everything actually landed and nudge the stragglers once more.
            System.Threading.Thread.Sleep(150);
            foreach (KeyValuePair<IntPtr, Rectangle> kv in placed)
            {
                Rectangle now;
                if (!Native.IsWindow(kv.Key) || !Native.TryGetWindowRect(kv.Key, out now)) continue;
                if (Math.Abs(now.X - kv.Value.X) > 2 || Math.Abs(now.Y - kv.Value.Y) > 2 ||
                    Math.Abs(now.Width - kv.Value.Width) > 2 || Math.Abs(now.Height - kv.Value.Height) > 2)
                    Native.Place(kv.Key, kv.Value, cfg.ResizeMethod);
            }
            WowWindow main = known[order[0]];
            lastTiled = DateTime.Now;
            Rectangle scr = Area();
            int fsCount = 0;
            foreach (IntPtr h in order) if (known[h].NeedsFix) fsCount++;
            if (fsCount > 0)
                SetStatus(fsCount + " client(s) are not in plain Windowed mode (blurry or immovable). Esc > System > Graphics > Display Mode = Windowed, or click 'Fix display mode'.");
            else if (Native.AccessDenied)
                SetStatus("Windows is blocking messages to WoW (WoW runs as administrator). Run WowTiler as administrator too.");
            else
                SetStatus(string.Format("Tiled {0} client(s).  Main: {1} at {2}x{3} ({4}% of screen width)",
                    order.Count, main.Label, rects[0].Width, rects[0].Height, (int)Math.Round(100.0 * rects[0].Width / scr.Width)));
            UpdatePreview();
            RebuildClientList();
        }

        // ---------------- raid mode ----------------
        // Main fills the whole monitor, borderless (looks like Windowed (Fullscreen) but
        // renders at full size). Alts keep their tiles underneath, or are minimized.

        void ToggleRaidMode()
        {
            if (raidMode) { raidMode = false; raidButton.Text = "Raid mode"; ApplyLayout(true); return; }
            if (order.Count == 0) { SetStatus("No clients to put in raid mode."); return; }
            EnterRaidMode();
        }

        void EnterRaidMode()
        {
            WowWindow main = known[order[0]];
            if (!Native.IsWindow(main.Hwnd)) return;
            raidMode = true;
            raidMain = main.Hwnd;
            raidButton.Text = "Leave raid mode";

            Screen scr = monitors[Math.Max(0, Math.Min(cfg.Monitor, monitors.Length - 1))];
            if (cfg.FocusOnSwap) { Native.ForceForeground(main.Hwnd); System.Threading.Thread.Sleep(150); }
            if (main.SetBorderless(true)) Native.CommitFrame(main.Hwnd);
            Native.Place(main.Hwnd, scr.Bounds, cfg.ResizeMethod);
            Native.BringToTop(main.Hwnd);

            for (int i = 1; i < order.Count; i++)
            {
                IntPtr h = order[i];
                if (!Native.IsWindow(h)) continue;
                if (cfg.RaidMinimizeAlts) Native.ShowWindow(h, Native.SW_SHOWMINNOACTIVE);
            }
            SetStatus("Raid mode: " + main.Label + " at " + scr.Bounds.Width + "x" + scr.Bounds.Height + ".  Press the hotkey or the button again to go back to the tiled layout.");
        }

        // Restore the border on the raid main before a normal layout runs.
        void LeaveRaidModeState()
        {
            raidMode = false;
            raidButton.Text = "Raid mode";
            if (pendingUpdate != null && pendingUpdate.Latest > Updater.Current)
            {
                var info = pendingUpdate;
                BeginInvoke((MethodInvoker)delegate { if (!Visible) ShowFromTray(); OfferUpdate(info); });
            }
            if (raidMain != IntPtr.Zero && known.ContainsKey(raidMain) && Native.IsWindow(raidMain))
            {
                WowWindow w = known[raidMain];
                if (!cfg.Borderless && w.SetBorderless(false)) Native.CommitFrame(raidMain);
            }
            for (int i = 0; i < order.Count; i++)
                if (Native.IsWindow(order[i]) && Native.IsIconic(order[i])) Native.ShowWindow(order[i], Native.SW_RESTORE);
            raidMain = IntPtr.Zero;
        }

        // WoW's "Windowed (Fullscreen)" renders at a fixed resolution and stretches it to the
        // window, so tool-resized windows come out blurry. Plain "Windowed" renders at the window
        // size. We set it in Config.wtf; the game reads that at startup.
        void FixDisplayMode()
        {
            foreach (WowWindow w in known.Values) w.ReadConfig();
            int bad = 0, fixedCount = 0, unknown = 0;
            foreach (WowWindow w in known.Values)
            {
                if (w.NeedsFix) { bad++; if (w.SetWindowedInConfig()) fixedCount++; }
                else if (w.Maximized == -1) unknown++;
            }
            string msg;
            if (bad == 0 && unknown == 0) msg = "All clients are already in Windowed mode.";
            else
            {
                msg = fixedCount + " of " + bad + " client config(s) switched to Windowed mode.";
                if (fixedCount > 0) msg += "\n\nWoW only reads this at startup: for clients that are open now, set it in-game (Esc > System > Graphics > Display Mode = Windowed, then Okay) or restart them.";
                if (unknown > 0) msg += "\n\n" + unknown + " client(s) could not be checked (config unreadable).";
            }
            MessageBox.Show(this, msg, "Fix display mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RebuildClientList();
        }

        void SetIgnored(IntPtr hwnd, bool ignored)
        {
            if (!known.ContainsKey(hwnd)) return;
            known[hwnd].Ignored = ignored;
            if (ignored) { if (raidMode && raidMain == hwnd) LeaveRaidModeState(); order.Remove(hwnd); }
            else if (!order.Contains(hwnd)) order.Add(hwnd);
            RebuildClientList();
            if (cfg.AutoApply && !raidMode && order.Count > 0) ApplyLayout(false);
        }

        int promoteTick = 0;
        IntPtr lastPromoted = IntPtr.Zero;

        // Promote a client into a main slot. With several mains, it replaces the one that was
        // promoted longest ago, so repeated promotions rotate through the mains.
        void MakeMain(IntPtr hwnd)
        {
            if (known.ContainsKey(hwnd) && known[hwnd].Ignored) SetIgnored(hwnd, false);
            int i = order.IndexOf(hwnd);
            if (i < 0 || i < MainCount) return;
            int target = 0, oldest = int.MaxValue;
            for (int s = 0; s < MainCount && s < order.Count; s++)
            {
                int tick = known.ContainsKey(order[s]) ? known[order[s]].PromoteTick : 0;
                if (tick < oldest) { oldest = tick; target = s; }
            }
            IntPtr tmp = order[target]; order[target] = order[i]; order[i] = tmp;
            if (known.ContainsKey(hwnd)) known[hwnd].PromoteTick = ++promoteTick;
            lastPromoted = hwnd;
            ApplyLayout(true);
        }

        void MakeSlotMain(int slot)
        {
            if (slot >= 0 && slot < order.Count) MakeMain(order[slot]);
        }

        void Rotate(int step)
        {
            int n = order.Count;
            if (n < 2) return;
            step = ((step % n) + n) % n;
            var rotated = new List<IntPtr>();
            for (int i = 0; i < n; i++) rotated.Add(order[(i + step) % n]);
            order = rotated;
            ApplyLayout(true);
        }

        void Poll()
        {
            var current = new HashSet<IntPtr>();
            foreach (WowWindow w in Native.FindWowWindows()) current.Add(w.Hwnd);
            if (!current.SetEquals(known.Keys))
            {
                int before = order.Count;
                if (raidMode)
                {
                    bool mainGone = !current.Contains(raidMain);
                    RefreshClients(false);
                    if (mainGone) { LeaveRaidModeState(); if (cfg.AutoApply) ApplyLayout(false); }
                    else SetStatus("Raid mode (" + order.Count + " client(s) now). Leave raid mode to re-tile.");
                    return;
                }
                RefreshClients(cfg.AutoApply);
                if (cfg.AutoApply)
                    SetStatus(string.Format("{0} -> {1} client(s): re-tiled at {2:HH:mm:ss}. ", before, order.Count, DateTime.Now) + statusLabel.Text);
            }
        }

        // ---------------- hotkeys ----------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitleBar(Handle);
            hotkeyHwnd = Handle;
            RegisterHotkeys();
        }

        void RegisterHotkeys()
        {
            if (hotkeyHwnd == IntPtr.Zero) return;
            foreach (int oldId in hotkeyActions.Keys) Native.UnregisterHotKey(hotkeyHwnd, oldId);
            hotkeyActions.Clear();
            int id = 1;
            var failed = new List<string>();
            foreach (string name in Config.HotkeyNames)
            {
                if (!cfg.Hotkeys.ContainsKey(name) || cfg.Hotkeys[name].Trim().Length == 0) continue;
                uint mods, vk;
                if (!HotkeyParser.TryParse(cfg.Hotkeys[name], out mods, out vk)) { failed.Add(cfg.Hotkeys[name] + " (bad)"); continue; }
                if (!Native.RegisterHotKey(hotkeyHwnd, id, mods | Native.MOD_NOREPEAT, vk)) { failed.Add(cfg.Hotkeys[name] + " (in use)"); continue; }
                hotkeyActions[id] = name;
                id++;
            }
            if (statusLabel == null) return;
            if (failed.Count > 0) SetStatus("Hotkeys not registered: " + string.Join(", ", failed.ToArray()));
            else if (!loading) SetStatus("Hotkeys updated.");
        }

        IntPtr hotkeyHwnd = IntPtr.Zero;

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyHwnd != IntPtr.Zero)
                foreach (int id in hotkeyActions.Keys) Native.UnregisterHotKey(hotkeyHwnd, id);
            hotkeyHwnd = IntPtr.Zero;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Native.WM_SHOWME)
            {
                Log.Write("WM_SHOWME received.");
                try { using (var ack = System.Threading.EventWaitHandle.OpenExisting("Local\\WowTiler.ShowAck")) ack.Set(); } catch (Exception) { }
                BeginInvoke((MethodInvoker)delegate { ShowFromTray(); });
                return;
            }
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = (int)(m.WParam.ToInt64() & 0xFFFF);
                string action;
                if (hotkeyActions.TryGetValue(id, out action))
                {
                    if (action == "next") Rotate(1);
                    else if (action == "prev") Rotate(-1);
                    else if (action == "retile") RefreshClients(true);
                    else if (action == "raid") ToggleRaidMode();
                    else if (action.StartsWith("slot")) MakeSlotMain(int.Parse(action.Substring(4)) - 1);
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                tray.ShowBalloonTip(1500, "WoW Window Tiler", "Still running in the tray. Right-click the icon to exit.", ToolTipIcon.Info);
                return;
            }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (fgHook != IntPtr.Zero) { Native.UnhookWinEvent(fgHook); fgHook = IntPtr.Zero; }
            pollTimer.Stop();
            // Give borders back so clients aren't left frameless after we exit
            // (not when restarting into an update: the new copy takes over in a second).
            foreach (WowWindow w in known.Values)
            {
                if (updating || !w.HasOrigStyle || !Native.IsWindow(w.Hwnd)) continue;
                w.SetBorderless(false);
                Native.SetWindowPos(w.Hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            }
            base.OnFormClosing(e);
        }
    }

    // -------------------------------------------------------------------------
    // Updater: asks GitHub for the latest release, downloads WowTiler.exe from it
    // as WowTiler.new.exe, then restarts (Program.Main swaps the file in).
    // -------------------------------------------------------------------------
    static class Updater
    {
        // GitHub "owner/repo" that hosts the releases. Set this before building a public release.
        public const string Repo = "Zac175/WowTiler";
        public const string AssetName = "WowTiler.exe";

        public class Info { public Version Latest; public string DownloadUrl = ""; public string Page = ""; public string Error = ""; }

        public static bool Configured { get { return Repo.IndexOf("YOURNAME", StringComparison.OrdinalIgnoreCase) < 0; } }

        static void PrepareTls()
        {
            // GitHub needs TLS 1.2; .NET Framework 4.x on Windows 10/11 supports it but 4.0-era defaults don't enable it.
            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)12288; }
            catch (Exception) { try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch (Exception) { } }
        }

        public static Info Check()
        {
            var info = new Info();
            if (!Configured) { info.Error = "No update source configured."; return info; }
            try
            {
                PrepareTls();
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers["User-Agent"] = "WowTiler/" + MainForm.Version;
                    wc.Headers["Accept"] = "application/vnd.github+json";
                    string json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                    var tag = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    var page = System.Text.RegularExpressions.Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                    if (!tag.Success) { info.Error = "Could not read the release info."; return info; }
                    string v = tag.Groups[1].Value.TrimStart('v', 'V');
                    if (v.IndexOf('.') < 0) v += ".0";
                    info.Latest = new Version(v);
                    info.Page = page.Success ? page.Groups[1].Value : "https://github.com/" + Repo + "/releases";
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                        if (m.Groups[1].Value.EndsWith("/" + AssetName, StringComparison.OrdinalIgnoreCase)) { info.DownloadUrl = m.Groups[1].Value; break; }
                    if (info.DownloadUrl.Length == 0) info.Error = "The latest release has no " + AssetName + " attached.";
                }
            }
            catch (Exception ex) { info.Error = ex.Message; }
            return info;
        }

        public static Version Current
        {
            get { string v = MainForm.Version; if (v.IndexOf('.') < 0) v += ".0"; return new Version(v); }
        }

        // Downloads to WowTiler.new.exe next to the running exe. Returns an error or "".
        public static string Download(string url)
        {
            try
            {
                PrepareTls();
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string tmp = Path.Combine(dir, "WowTiler.download.tmp");
                string dest = Path.Combine(dir, "WowTiler.new.exe");
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers["User-Agent"] = "WowTiler/" + MainForm.Version;
                    wc.DownloadFile(url, tmp);
                }
                var fi = new FileInfo(tmp);
                if (fi.Length < 20000) { File.Delete(tmp); return "Downloaded file looks wrong (too small)."; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static string PathName { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "WowTiler.log"); } }
        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    var fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();          // keep it small
                    File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch (Exception) { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.ThreadException += delegate(object s2, System.Threading.ThreadExceptionEventArgs e2) { Log.Write("UI exception: " + e2.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s2, UnhandledExceptionEventArgs e2) { Log.Write("Fatal exception: " + e2.ExceptionObject); };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool minimized = false;
            int waitForPid = 0;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized" || args[i] == "/minimized") minimized = true;
                if (args[i] == "--wait-for" && i + 1 < args.Length) int.TryParse(args[i + 1], out waitForPid);
            }

            // Restarted by the updater: let the previous copy finish exiting first.
            if (waitForPid != 0)
            {
                try { Process.GetProcessById(waitForPid).WaitForExit(15000); } catch (Exception) { }
            }

            // One copy at a time: launching the exe again just brings the running one up.
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Local\\WowTiler.SingleInstance", out created))
            {
                if (!created)
                {
                    Log.Write("Second instance started; asking the running copy to show itself.");
                    Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                    bool acked = false;
                    try
                    {
                        using (var ack = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\WowTiler.ShowAck"))
                        {
                            Native.PostMessageW(Native.HWND_BROADCAST, Native.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                            acked = ack.WaitOne(2500);
                        }
                    }
                    catch (Exception ex) { Log.Write("Ack wait failed: " + ex.Message); }
                    if (!acked)
                    {
                        Log.Write("Running copy did not respond within 2.5 s.");
                        MessageBox.Show("WoW Window Tiler is already running but did not respond.\n\nLook for its icon in the system tray (the ^ overflow). If it is stuck, end WowTiler.exe in Task Manager and start it again.\n\nDetails are in WowTiler.log next to the exe.",
                            "WoW Window Tiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                // Self-update: if WowTiler.new.exe sits next to us, swap it in and relaunch.
                // (Only when we are the sole copy; otherwise the old process would keep running.)
                try
                {
                    string exe = Application.ExecutablePath;
                    string dir = Path.GetDirectoryName(exe);
                    string fresh = Path.Combine(dir, "WowTiler.new.exe");
                    if (File.Exists(fresh) && !exe.EndsWith(".new.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string old = Path.Combine(dir, "WowTiler.old.exe");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(exe, old);
                        File.Move(fresh, exe);
                        Log.Write("Swapped in WowTiler.new.exe; relaunching.");
                        Process.Start(exe, "--wait-for " + Process.GetCurrentProcess().Id + (minimized ? " --minimized" : ""));
                        return;
                    }
                    string stale = Path.Combine(dir, "WowTiler.old.exe");
                    if (File.Exists(stale)) { try { File.Delete(stale); } catch (Exception) { } }
                }
                catch (Exception ex) { Log.Write("Self-update failed: " + ex.Message); }

                Log.Write("Started version " + MainForm.Version + (minimized ? " (minimized)" : ""));
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(minimized));
            }
        }
    }
}
