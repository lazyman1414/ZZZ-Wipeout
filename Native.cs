using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZzzWipeout
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwnd; public uint dwHoverTime; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }

        [DllImport("user32")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32")] public static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr prev);
        [DllImport("gdi32")] public static extern bool GdiFlush();
        [DllImport("gdi32")] public static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("gdi32")] public static extern int GetDeviceCaps(IntPtr dc, int index);

        [DllImport("user32")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT pt, ref SIZE sz, IntPtr srcDc, ref POINT srcPt, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("user32", EntryPoint = "UpdateLayeredWindow")] public static extern bool UpdateLayeredWindowNoPos(IntPtr hwnd, IntPtr dstDc, IntPtr pt, ref SIZE sz, IntPtr srcDc, ref POINT srcPt, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("user32")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
        [DllImport("user32")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32")] public static extern bool ReleaseCapture();
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
        [DllImport("user32")] static extern bool EnumWindows(EnumProc proc, IntPtr lp);

        [DllImport("kernel32")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);

        public const uint SRCCOPY = 0x00CC0020;

        // 32비트 위아래가 바른(top-down) DIB를 만든다. AutoHotkey 쪽 CreateDIBSection과 같은 형식
        public static IntPtr NewDib(IntPtr dc, int w, int h, out IntPtr bits)
        {
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            return CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
        }

        public static int ScreenW => GetSystemMetrics(0);
        public static int ScreenH => GetSystemMetrics(1);

        static double dpi;
        public static double ScreenDpi
        {
            get
            {
                if (dpi == 0)
                {
                    IntPtr dc = GetDC(IntPtr.Zero);
                    dpi = GetDeviceCaps(dc, 88);  // LOGPIXELSX
                    ReleaseDC(IntPtr.Zero, dc);
                }
                return dpi;
            }
        }

        // ---- 게임 창 (ahk_exe ZenlessZoneZero.exe) ----
        const string GAME_EXE = "ZenlessZoneZero.exe";
        static readonly Dictionary<uint, bool> isGamePid = new Dictionary<uint, bool>();

        static bool IsGame(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return false;
            if (isGamePid.TryGetValue(pid, out bool g))
                return g;
            g = false;
            IntPtr h = OpenProcess(0x1000, false, pid);  // PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref n))
                    g = string.Equals(Path.GetFileName(sb.ToString()), GAME_EXE, StringComparison.OrdinalIgnoreCase);
                CloseHandle(h);
                if (isGamePid.Count > 500)
                    isGamePid.Clear();
                isGamePid[pid] = g;  // 열지 못한 프로세스는 기억하지 않고 다음에 다시 본다
            }
            return g;
        }

        // WinActive(GAME)
        public static bool GameActive()
        {
            IntPtr fg = GetForegroundWindow();
            return fg != IntPtr.Zero && IsGame(fg);
        }

        // 게임 창: 앞에 있으면 그 창, 아니면 보이는 창 중 가장 위의 게임 창 (AutoHotkey의 WinExist 순서와 같다)
        public static IntPtr GameWindow()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && IsGame(fg))
                return fg;
            IntPtr found = IntPtr.Zero;
            EnumWindows((hw, lp) =>
            {
                if (IsWindowVisible(hw) && IsGame(hw))
                {
                    found = hw;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // WinGetClientPos(GAME): 게임 창 안쪽(제목 표시줄 제외)의 화면 좌표와 크기
        public static bool GameClient(out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            IntPtr hw = GameWindow();
            if (hw == IntPtr.Zero || !GetClientRect(hw, out RECT r))
                return false;
            var p = new POINT();
            ClientToScreen(hw, ref p);
            x = p.X; y = p.Y; w = r.R - r.L; h = r.B - r.T;
            return true;
        }

        public static bool CursorShown()
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            return GetCursorInfo(ref ci) && (ci.flags & 1) != 0;
        }
    }
}
