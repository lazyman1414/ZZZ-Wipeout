using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static ZzzWipeout.Native;

namespace ZzzWipeout
{
    // 클릭 검사: 게임 위에 띄운 우리 창을 눌렀을 때 게임이 그 클릭을 받는지 본다 (게임 없이).
    //   --clicktest 결과파일 방법(none|grab) [sink] : 가짜 게임을 관리자 권한으로 띄우고(실제 게임과 같음), 그 위 화면 가운데에
    //       우리 창(게임 위 창)을 띄운다. 가짜 게임이 끝나면 우리 창이 눌렸는지와 가짜 게임의 기록을 결과파일에 남긴다
    //   --fakegame 기록파일 [sink] : 가짜 게임. 화면 전체 검은 창. 마우스를 게임처럼 Raw Input으로 읽어 누름을 기록한다
    //       (sink면 뒤에 있어도 받음). 관리자 권한 창이 앞에 있으면 일반 권한 프로그램은 마우스 입력을 흉내 낼 수 없어서,
    //       마우스 움직임·클릭은 가짜 게임이 직접 보낸다: 창 밖 → 가운데 버튼으로 들어가 클릭 → 다시 창 밖
    static class ClickTest
    {
        public static void Run(string[] args)
        {
            if (args[0] == "--fakegame")
                Application.Run(new FakeGame(args[1], Array.IndexOf(args, "sink") > 0, Array.IndexOf(args, "alttab") > 0));
            else
                Test(args[1], args[2], Array.IndexOf(args, "sink") > 0, Array.IndexOf(args, "alttab") > 0, Array.IndexOf(args, "noowner") > 0);
        }

        sealed class FakeGame : Form
        {
            [StructLayout(LayoutKind.Sequential)] struct RID { public ushort page, usage; public uint flags; public IntPtr target; }
            [DllImport("user32")] static extern bool RegisterRawInputDevices(RID[] d, uint n, uint size);
            [DllImport("user32")] static extern uint GetRawInputData(IntPtr h, uint cmd, byte[] data, ref uint size, uint header);
            readonly string log;
            readonly bool sink, alttab;
            volatile bool started;

            public FakeGame(string log, bool sink, bool alttab)
            {
                this.log = log;
                this.sink = sink;
                this.alttab = alttab;
                Text = "가짜 게임";
                FormBorderStyle = FormBorderStyle.None;
                BackColor = Color.Black;
                StartPosition = FormStartPosition.Manual;
                Bounds = Screen.PrimaryScreen.Bounds;
                File.WriteAllText(log, "");
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                var d = new[] { new RID { page = 1, usage = 2, flags = sink ? 0x100u : 0u, target = Handle } };  // 마우스, RIDEV_INPUTSINK
                RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RID)));
                IntPtr me = Handle;
                new Thread(() =>
                {
                    Thread.Sleep(1500);  // 그사이 검사 쪽이 우리 창을 띄운다
                    started = true;
                    MoveTo(ScreenW / 2 - 300, ScreenH / 2 + 300);
                    Thread.Sleep(200);
                    MoveTo(ScreenW / 2 - 10, ScreenH / 2 - 10);
                    Thread.Sleep(400);
                    Note("누를 때 앞 창 = " + (GetForegroundWindow() == me ? "가짜 게임" : "다른 창"));
                    Mouse(0x0002);  // LEFTDOWN
                    Thread.Sleep(60);
                    Mouse(0x0004);  // LEFTUP
                    Thread.Sleep(300);
                    Note("뗀 뒤 커서가 " + (Math.Abs(Cursor.Position.X - (ScreenW / 2 - 10)) < 3 && Math.Abs(Cursor.Position.Y - (ScreenH / 2 - 10)) < 3 ? "원래 자리로 돌아옴" : "다른 곳"));
                    if (alttab)  // 우리 창이 포커스를 가진 채 알트탭: 게임과 함께 내려가는지, 다시 알트탭하면 함께 올라오는지
                    {
                        AltTab(me, "알트탭(내리기)");
                        AltTab(me, "알트탭(돌아오기)");
                    }
                    MoveTo(ScreenW / 2 - 300, ScreenH / 2 + 300);
                    Thread.Sleep(500);
                    Note("나간 뒤 앞 창 = " + (GetForegroundWindow() == me ? "가짜 게임" : "다른 창") + ", 커서가 " + (Cursor.Position.Y > ScreenH / 2 + 200 ? "제자리" : "다른 곳"));
                    BeginInvoke((Action)Close);
                }) { IsBackground = true }.Start();
            }

            void Note(string s) => File.AppendAllText(log, s + "\n");

            [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
            [DllImport("user32")] static extern IntPtr WindowFromPoint(Point p);
            [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
            [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

            string Who(IntPtr h, IntPtr me, IntPtr ours)
            {
                if (h == me) return "가짜 게임";
                if (h == ours) return "우리 창";
                if (h == IntPtr.Zero) return "없음";
                GetWindowThreadProcessId(h, out uint pid);
                try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
            }

            // Alt+Tab을 누르고 0.8초 동안 앞 창과 우리 창이 보이는지의 바뀜을 기록한 뒤, 앞 창과 화면 가운데 맨 위 창을 남긴다
            void AltTab(IntPtr me, string name)
            {
                IntPtr ours = FindWindow(null, "클릭 검사 창");
                int ex = GetWindowLong(ours, -20);  // 알트탭 목록에 나오는 조건: APPWINDOW가 있거나, 주인이 없고 TOOLWINDOW가 아님
                bool inList = (ex & 0x40000) != 0 || GetWindow(ours, 4) == IntPtr.Zero && (ex & 0x80) == 0;
                Note($"{name} 전: 우리 창 알트탭 목록에 {(inList ? "나옴" : "안 나옴")}, 우리 창 주인 = {Who(GetWindow(ours, 4), me, ours)}, 앞 창 = {Who(GetForegroundWindow(), me, ours)}, 가운데 맨 위 = {Who(GetAncestor(WindowFromPoint(new Point(ScreenW / 2, ScreenH / 2)), 2), me, ours)}");
                Key(0x12, false); Thread.Sleep(100); Key(0x09, false); Thread.Sleep(30); Key(0x09, true); Thread.Sleep(30); Key(0x12, true);
                string seen = "", lastFg = "", lastVis = "";
                for (int i = 0; i < 40; i++)
                {
                    string fg = Who(GetForegroundWindow(), me, ours), vis = IsWindowVisible(ours) ? "보임" : "숨음";
                    if (fg != lastFg || vis != lastVis)
                        seen += $" → 앞 {fg}·우리 창 {vis}";
                    lastFg = fg; lastVis = vis;
                    Thread.Sleep(20);
                }
                IntPtr fgw = GetForegroundWindow();
                bool above = false;  // 우리 창이 지금 앞 창보다 위(창 순서)에 있나
                for (IntPtr h = GetWindow(fgw, 3); h != IntPtr.Zero; h = GetWindow(h, 3))  // GW_HWNDPREV
                    if (h == ours) { above = true; break; }
                Note($"{name}:{seen} / 앞 창 = {Who(fgw, me, ours)}, 우리 창이 앞 창보다 위 = {(fgw == ours ? "(우리 창이 앞)" : above ? "예" : "아니오")}");
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0xFF)  // WM_INPUT
                {
                    uint size = 0, hs = (uint)(IntPtr.Size == 8 ? 24 : 16);
                    GetRawInputData(m.LParam, 0x10000003, null, ref size, hs);
                    var b = new byte[size];
                    if (GetRawInputData(m.LParam, 0x10000003, b, ref size, hs) == size && BitConverter.ToUInt32(b, 0) == 0)  // RIM_TYPEMOUSE
                    {
                        ushort bf = BitConverter.ToUInt16(b, (int)hs + 4);  // usButtonFlags
                        if ((bf & 3) != 0)  // 누름(1)·뗌(2): 게임처럼 다음 프레임(30ms 뒤)에 커서 위치를 읽어 누를 대상을 정한다
                        {
                            string what = (bf & 1) != 0 ? "누름" : "뗌", fgs = GetForegroundWindow() == Handle ? "앞" : "뒤";
                            new Thread(() =>
                            {
                                Thread.Sleep(30);
                                var c = Cursor.Position;
                                bool onBtn = Math.Abs(c.X - ScreenW / 2) < 200 && Math.Abs(c.Y - ScreenH / 2) < 110;
                                Note($"게임이 {what} 받음 (raw, {fgs}) → 커서 위치 {(onBtn ? "우리 창 위(그 아래 게임 버튼이 눌림)" : c.Y < 10 ? "맨 위 빈 곳" : "다른 곳")}");
                            }) { IsBackground = true }.Start();
                        }
                    }
                }
                else if (m.Msg == 0x201)
                    Note("게임이 클릭 받음 (창 메시지)");
                else if (started && (m.Msg == 0x6 || m.Msg == 0x1C || m.Msg == 0x7 || m.Msg == 0x8 || m.Msg == 0xA))  // 포커스·활성 변화 (음소거 원인)
                    Note((m.Msg == 0x6 ? "WM_ACTIVATE " : m.Msg == 0x1C ? "WM_ACTIVATEAPP " : m.Msg == 0x7 ? "WM_SETFOCUS " : m.Msg == 0x8 ? "WM_KILLFOCUS " : "WM_ENABLE ") + ((long)m.WParam & 0xFFFF));
                base.WndProc(ref m);
            }
        }

        sealed class TestWindow : UiWindow
        {
            public string Got = "";
            public TestWindow() : base("클릭 검사 창", true) { Rects.Add(("btn", new RectangleF(50, 50, 300, 120))); }

            public override void Draw(int? x = null, int? y = null)
            {
                var c = new Canvas(400, 220);
                using (var br = new SolidBrush(Color.FromArgb(246, 8, 8, 8)))
                    c.G.FillRectangle(br, 0, 0, 400, 220);
                D.BtnPaint(c.G, 300, 120, 1440, "검사 버튼", x: 50, y: 50, hot: Hot == "btn");
                c.End(Handle, x, y);
            }

            public override void Act(string act) => Got += act + ";";
            public override void CloseUi() { }
        }

        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy, data; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }
        [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [StructLayout(LayoutKind.Explicit, Size = 40)] struct KINPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public ushort vk; [FieldOffset(12)] public uint flags; }
        [DllImport("user32", EntryPoint = "SendInput")] static extern uint SendKeys(uint n, KINPUT[] inputs, int size);

        static void Key(ushort vk, bool up) => SendKeys(1, new[] { new KINPUT { type = 1, vk = vk, flags = up ? 2u : 0u } }, 40);

        static void Mouse(uint flags, int x = 0, int y = 0)
        {
            var i = new INPUT { type = 0, mi = new MOUSEINPUT { dx = x, dy = y, flags = flags } };
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }

        static void MoveTo(int x, int y) => Mouse(0x8001, (x * 65535 + ScreenW / 2) / (ScreenW - 1), (y * 65535 + ScreenH / 2) / (ScreenH - 1));  // MOVE|ABSOLUTE

        static void Test(string outFile, string mode, bool sink, bool alttab, bool noOwner)
        {
            string glog = Path.Combine(Path.GetTempPath(), "ZZZ-Wipeout-fakegame.txt");
            App.GrabOn = mode == "grab";
            var game = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--fakegame \"" + glog + "\"" + (sink ? " sink" : "") + (alttab ? " alttab" : "")) { UseShellExecute = true, Verb = "runas" });
            IntPtr gh = IntPtr.Zero;
            UiWindow.OverlayOwner = () =>  // 우리 창의 주인은 가짜 게임 창
            {
                for (int i = 0; i < 50 && gh == IntPtr.Zero; i++)
                {
                    try { game.Refresh(); gh = game.MainWindowHandle; } catch { }
                    if (gh == IntPtr.Zero) Thread.Sleep(20);
                }
                return noOwner ? IntPtr.Zero : gh;  // noowner: 예전처럼 주인 없는 맨 위 창
            };
            var w = new TestWindow();
            var ctx = new ApplicationContext();  // 보이는 보조 창 없이 메시지 루프만 (보조 창이 알트탭 목록에 끼지 않게)
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (s2, e2) =>
            {
                t.Stop();
                w.Open(ScreenW / 2 - 200, ScreenH / 2 - 110);
                new Thread(() =>
                {
                    game.WaitForExit(20000);
                    w.Invoke((Action)(() => { w.Close(); ctx.ExitThread(); }));
                }) { IsBackground = true }.Start();
            };
            t.Start();
            Application.Run(ctx);
            Thread.Sleep(200);
            string got = File.Exists(glog) ? File.ReadAllText(glog).Trim().Replace("\n", " / ") : "";
            string elev = IsElevated() ? "관리자" : "일반";
            File.AppendAllText(outFile, $"방법 {mode}{(sink ? " +sink" : "")}{(noOwner ? " +주인 없음" : "")} (프로그램 {elev} 권한): 우리 창 눌림 = {(w.Got.Contains("btn") ? "예" : "아니오")} / {got}\n");
        }

        static bool IsElevated() => new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
