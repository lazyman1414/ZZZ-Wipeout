using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using static ZzzWipeout.Native;
using FormsTimer = System.Windows.Forms.Timer;

namespace ZzzWipeout
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "--replay")
            {
                Replay.Run(args[1]);
                return;
            }
            if (args.Length > 2 && args[0] == "--resulttest")
            {
                Replay.ResultTest(args);
                return;
            }
            if (args.Length > 1 && args[0] == "--perftest")
            {
                System.IO.File.WriteAllText(args[1], Wipe.Perf() + "\n");
                return;
            }
            if (args.Length > 1 && (args[0] == "--clicktest" || args[0] == "--fakegame"))
            {
                ClickTest.Run(args);
                return;
            }
            Cfg.BootFix();
            bool logon = args.Length > 0 && string.Equals(args[0], "--logon", StringComparison.OrdinalIgnoreCase);
            uint showMsg = RegisterWindowMessage("ZZZ_WIPEOUT_SHOW");
            // 이미 떠 있으면 그 프로그램에 설정 창을 열라고 알리고 끝낸다 (로그인 자동 실행일 때는 조용히 끝낸다)
            using (var mutex = new Mutex(true, "ZZZ-Wipeout-SingleInstance", out bool created))
            {
                if (!created)
                {
                    if (!logon)
                        PostMessage((IntPtr)0xFFFF, showMsg, IntPtr.Zero, IntPtr.Zero);  // HWND_BROADCAST
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                App.Run(logon, showMsg);
            }
        }
    }

    // 다른 실행에서 보낸 "설정 창 열기" 알림을 받는 보이지 않는 최상위 창
    sealed class MsgHost : NativeWindow
    {
        readonly uint msg;
        public MsgHost(uint msg) { this.msg = msg; CreateHandle(new CreateParams { Caption = "ZZZ-Wipeout-Host" }); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == msg)
                App.ShowSettings();
            base.WndProc(ref m);
        }
    }

    static class App
    {
        public static ViewWindow WV;
        public static PhotoWindow PV;
        public static SettingsWindow SV;
        static ResultButton btn;
        static NotifyIcon tray;
        static MsgHost host;

        public static void Run(bool logon, uint showMsg)
        {
            Cfg.Load();
            host = new MsgHost(showMsg);
            btn = new ResultButton();
            Capture(btn.Handle);

            // 트레이 아이콘: 더블클릭 = 설정, 우클릭 = 메뉴
            var menu = new ContextMenuStrip();
            var setItem = menu.Items.Add("설정", null, (s, e) => ShowSettings());
            setItem.Font = new Font(setItem.Font, FontStyle.Bold);
            menu.Items.Add("최근 캡처 보기", null, (s, e) => ShowWipe());
            menu.Items.Add("저장 폴더 열기", null, (s, e) => OpenDir());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (s, e) => Quit());
            tray = new NotifyIcon { Icon = LoadIcon(), Text = "ZZZ 와이프아웃 캡처", ContextMenuStrip = menu, Visible = GameWindow() != IntPtr.Zero };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

            // 게임 창이 앞에 있을 때만 0.1초마다 화면을 본다
            var watch = new FormsTimer { Interval = 100 };
            watch.Tick += (s, e) => Watch();
            watch.Start();
            if (!logon)  // 직접 실행했으면 설정 창을 연다 (처음 실행이면 설정 파일도 만든다)
            {
                if (Cfg.FirstRun)
                    Cfg.Save();
                ShowSettings();
            }
            Application.Run();
        }

        static Icon LoadIcon()
        {
            try
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    return new Icon(st, SystemInformation.SmallIconSize);
            }
            catch { return SystemIcons.Application; }
        }

        static void Quit()
        {
            tray.Visible = false;
            Application.Exit();
        }

        // 녹화에 보이기(설정)를 끄면 화면 캡처·녹화에서 뺀다. 결과 화면 버튼은 판별 지점(위쪽 줄, "완료" 글자)과 겹치지 않아 켜도 판별에 지장 없다
        public static void Capture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, Cfg.Rec ? 0u : 0x11u);

        public static void CaptureAll()
        {
            Capture(btn.Handle);
            if (WV != null && WV.Overlay)
                Capture(WV.Handle);
            if (PV != null && PV.Overlay)
                Capture(PV.Handle);
        }

        // 게임 창이 앞에 있을 때만 판별한다. 다른 창으로 가면 결과 화면 버튼을 숨긴다
        static void Watch()
        {
            // 트레이 아이콘은 게임이 실행 중일 때만 보인다 (단축키 스크립트와 같게). 꺼져 있을 때 설정은 exe를 다시 실행해 연다
            bool exists = GameWindow() != IntPtr.Zero;
            if (tray.Visible != exists)
                tray.Visible = exists;
            if (!GameActive())
            {
                BtnHide();
                return;
            }
            try
            {
                if (!Wipe.On && Wipe.Top(CursorShown()))
                    Wipe.Start();
                WipeButton();
            }
            catch (Exception e)
            {
                Wipe.Log("오류: " + e.Message);
            }
        }

        // ---- 게임 위 창의 포커스 ----
        // 게임은 앞에 있는 동안 마우스 버튼을 Raw Input으로 직접 받아서, 포커스를 안 뺏는 우리 창을 눌러도 그 아래 게임 버튼·아이콘이
        // 같이 눌렸다 (훅·창 비활성으로는 못 막음). 마우스가 우리 창에 들어오면 우리 창을 앞으로 가져오고(게임은 뒤에 있는 동안
        // 클릭을 받지 않음), 나가면 게임에 돌려준다. 그동안 게임의 백그라운드 음소거가 켜질 수 있다
        public static bool GrabOn = true;  // 클릭 검사(ClickTest)에서 끈다
        static IntPtr gameFg;
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static void Grab(UiWindow w)
        {
            if (!GrabOn)
                return;
            // 게임(이 창의 주인)이 앞에 있을 때만 가져온다. 알트탭으로 다른 프로그램이 앞에 오면 Windows가 커서 아래 창에 마우스 이동을
            // 다시 보내는데, 그때 가져오면 알트탭이 되돌려졌다
            IntPtr fg = GetForegroundWindow(), own = GetWindow(w.Handle, 4);  // GW_OWNER
            if (fg == w.Handle || !(own != IntPtr.Zero ? fg == own : IsGame(fg)))
                return;
            gameFg = fg;
            bool ok = ForceForeground(w.Handle);
            if (!ok)  // 포커스를 안 받는 창(WS_EX_NOACTIVATE)이라 거절되면 그 표시를 떼고 다시
            {
                SetWindowLong(w.Handle, -20, GetWindowLong(w.Handle, -20) & ~0x08000000);
                ok = ForceForeground(w.Handle);
            }
            if (!ok)
                Wipe.Log("게임 위 창이 포커스를 못 가져옴 (게임이 클릭을 같이 받을 수 있음)");
        }

        // 마우스가 창을 벗어날 때, 그 창이 아직 포커스를 갖고 있으면 게임에 돌려준다 (그사이 설정 창 등으로 옮겨 갔으면 그대로)
        public static void Ungrab(UiWindow w)
        {
            if (GetForegroundWindow() == w.Handle)
                GiveBack();
        }

        // 게임 위 창을 닫을 때 그 창이 포커스를 갖고 있었으면 게임에 돌려준다
        static void ReturnFocus(Form f)
        {
            if (f != null && f.IsHandleCreated && GetForegroundWindow() == f.Handle)
                GiveBack();
        }

        public static void GiveBack()
        {
            IntPtr g = gameFg != IntPtr.Zero && IsWindow(gameFg) ? gameFg : GameWindow();  // 가져올 때 게임이 앞에 없었으면 게임 창을 찾아서
            if (g != IntPtr.Zero)
                ForceForeground(g);
        }

        public static bool Grabbed(IntPtr fg)
        {
            GetWindowThreadProcessId(fg, out uint pid);
            return pid == myPid && ((WV != null && WV.Overlay) || (PV != null && PV.Overlay));
        }

        // ---- 결과 화면 버튼 ----

        // 와이프아웃을 찍은 뒤 도전 결과 화면("완료" 글자)이 보이면 버튼을 띄운다.
        // 결과 화면을 한 번 보고 벗어나면 버튼을 끝낸다 (결과 화면이 2분 안에 안 나와도 끝)
        static int lastLeft = -1;
        static int same, away;

        static void WipeButton()
        {
            if (!Wipe.Ready)
                return;
            if (!Cfg.Btn)  // 설정에서 끄면 버튼을 띄우지 않는다 (캡처와 자동 저장은 그대로)
            {
                BtnHide();
                return;
            }
            if (ResultScreen())
            {
                away = 0;
                Wipe.Seen = true;
                if (IsWindowVisible(btn.Handle))
                    return;
                // 버튼 줄이 다 나타날 때까지 기다린다: 가장 왼쪽 끝이 0.1초 간격으로 세 번 같으면 그 왼쪽에 띄운다
                if (!Wipe.GameView(out int gx, out int gy, out int w, out int h))
                    return;
                int left = BtnLeft(gx, gy, w, h);
                same = Math.Abs(left - lastLeft) <= h * 0.009 ? same + 1 : 0;
                lastLeft = left;
                if (same >= 2)
                    BtnShow(left);
                return;
            }
            lastLeft = -1;
            same = 0;
            // 결과 화면을 한 번 본 뒤에는 1.5초 넘게 이어서 안 보여야 벗어난 것으로 본다. 마우스를 올린 게임 버튼, 잠깐 뜬 팝업,
            // 우리 창이 판별 지점을 가린 순간 등으로 한두 번 틀리면 버튼과 확인 창이 사라져 다시 안 떴다
            if (Wipe.Seen && ++away < 15)
                return;
            BtnHide();
            if (Wipe.Seen && WV != null && WV.Overlay)  // "한 번 더"·"완료"로 결과 화면을 벗어나면 게임 위에 띄운 확인 창도 닫는다
                ViewClose();
            // 버튼은 15초 동안 결과 화면이 다시 나오기를 기다린다 (게임 팝업이 결과 화면을 잠깐 덮었다 닫힌 경우 다시 띄움)
            if ((Wipe.Seen && away >= 150) || Environment.TickCount - Wipe.EndTick > 120000)
                Wipe.Ready = false;
        }

        static void BtnHide()
        {
            if (IsWindowVisible(btn.Handle))
                ShowWindow(btn.Handle, 0);
        }

        // ---- 결과 화면의 위치 기준 ----
        // 결과 화면 아래 버튼 줄은 게임 화면 오른쪽 끝에 붙어 있고 크기는 화면 높이에 비례한다 (게임 UI가 높이 기준으로 늘고 줄어듦).
        // 그래서 가로 위치는 "게임 영역 오른쪽 끝에서 높이의 몇 배" (A), 세로 위치는 "높이의 몇 배"로 잰다.
        // 값은 16:9(2000x1125) 결과 화면에서 잰 가로 비율 x를 A = (1 - x) * 16 / 9로 바꾼 것. 모니터 비율이 달라 게임이
        // 양옆을 비우든(GameView가 뺌) 넓게 그리든 같은 자리를 가리킨다
        static int RX(int w, int h, double a) => w - Wipe.R(h * a);  // 게임 영역 안 x

        // 결과 화면 아래 줄에서 게임이 그린 것(버튼, "한 번 더" 왼쪽에 붙는 배터리 칸 등) 중 가장 왼쪽 끝(게임 영역 안 x)을 돌려준다.
        // 결과마다 왼쪽에 붙는 것이 달라서 매번 잰다. 바탕은 검정(밝기 0), 버튼은 테두리·글자가 밝고 속은 위아래로 밝기가 달라서
        // 버튼 높이(0.932~0.977h)를 세로로 훑은 최댓값이 20을 넘으면 무언가 있는 칸으로 본다.
        // "완료" 버튼 안(A 0.213)에서 왼쪽으로 1px씩 따라가다 빈 칸이 0.053h보다 길게 이어지면(버튼 간격은 0.020h) 멈춘다
        static int BtnLeft(int gx, int gy, int w, int h)
        {
            int rows = Wipe.R(h * 0.045);
            int[] buf = Wipe.GrabLum(gx, gy + Wipe.R(h * 0.932), w, rows);
            int left = RX(w, h, 0.2133), dark = 0, gapMax = Wipe.R(h * 0.0533);
            for (int x = left - 1; x > 0; x--)
            {
                int m = 0;
                for (int i = 0; i < rows; i++)
                    m = Math.Max(m, Lum(buf[i * w + x]));
                if (m > 20)
                {
                    left = x;
                    dark = 0;
                }
                else if (++dark > gapMax)
                    break;
            }
            return left;
        }

        static int Lum(int c) => (((c >> 16) & 0xFF) * 77 + ((c >> 8) & 0xFF) * 150 + (c & 0xFF) * 29) >> 8;

        // 오른쪽 끝 게임 버튼("완료" 자리)을 재서 위쪽 끝(게임 영역 안 y), 높이, 폭, 왼쪽 버튼과의 간격을 돌려준다.
        // 글자와 상관없이 버튼 모양(검은 바탕 위 알약형 테두리)만 본다. 못 재면 false.
        // 버튼 속은 밝기 0~18, 테두리는 40~77이라 28을 넘으면 테두리로 본다
        static bool DoneBox(int gx, int gy, int w, int h, out int top, out int bh, out int bw, out int gap)
        {
            top = bh = bw = gap = 0;
            int y0 = Wipe.R(h * 0.90), rows = h - y0;
            int[] buf = Wipe.GrabLum(gx, gy + y0, w, rows);
            // 글자를 피한 세로줄을 아래에서 위로: 검은 바탕 → 아래 테두리 → 속 → 위 테두리.
            // 버튼 위쪽은 게임 배경이 밝게 보일 수 있어서 위에서부터 재지 않는다. 글자가 길어 걸릴 때를 위해 양쪽 두 줄을 차례로 본다
            // (둥근 끝(버튼 높이의 절반) 안쪽이어야 높이가 제대로 잡힌다)
            int t = 0, b = 0;
            foreach (double a in new[] { 0.3022, 0.0889 })
            {
                int cx = RX(w, h, a);
                if (cx < 0 || cx >= w)
                    continue;
                bool Lit(int i) => Lum(buf[i * w + cx]) > 28;
                b = rows - 1;
                while (b > 0 && !Lit(b)) b--;
                t = b;
                while (t > 0 && Lit(t - 1)) t--;
                while (t > 0 && !Lit(t - 1)) t--;
                while (t > 0 && Lit(t - 1)) t--;
                if (b > 0 && t > 0 && b - t + 1 >= h * 0.045 && b - t + 1 <= h * 0.062)
                    break;
                b = 0;
            }
            if (b == 0)
                return false;
            int[] row = new int[w];
            Array.Copy(buf, ((t + b) / 2) * w, row, 0, w);  // 가운데 줄: 둥근 양 끝이 세로로 서 있다
            int Edge(int from, int dir)  // from에서 dir 쪽으로 속(어두움)을 지나 테두리가 끝나는 곳
            {
                int x = from;
                while (x > 0 && x < w - 1 && Lum(row[x]) <= 28) x += dir;
                while (x > 0 && x < w - 1 && Lum(row[x + dir]) > 28) x += dir;
                return x;
            }
            int l = Edge(Math.Max(1, RX(w, h, 0.3289)), -1), r = Edge(Math.Max(1, RX(w, h, 0.1244)), 1);
            int prevR = l - 1;
            while (prevR > 0 && Lum(row[prevR]) <= 28) prevR--;  // 왼쪽 버튼("한 번 더")의 오른쪽 끝
            top = y0 + t; bh = b - t + 1; bw = r - l + 1; gap = l - prevR - 1;
            return bw > h * 0.2667 && bw < h * 0.3022 && gap > h * 0.0089 && gap < h * 0.0356;
        }

        // 결과 화면 버튼의 자리(화면 좌표): 게임 버튼 줄의 가장 왼쪽(left, 게임 영역 안 x) 바로 왼쪽에 "한 번 더"와 같은 크기·같은 줄
        // (못 재면 16:9 결과 화면에서 잰 값: 한 번 더 폭 0.2827h, 0.9271~0.9787h, 버튼 간격 0.0204h)
        public static Rectangle BtnRect(int gx, int gy, int w, int h, int left)
        {
            int bw = Wipe.R(h * 0.2827), bh = Wipe.R(h * 0.0533), y = Wipe.R(h * 0.9271), gap = Wipe.R(h * 0.0204);
            if (DoneBox(gx, gy, w, h, out int dt, out int dh, out int dw, out int dg))
            {
                y = dt; bh = dh; bw = dw; gap = dg;  // 게임 버튼과 크기·줄·간격을 px까지 같게
            }
            return new Rectangle(gx + Math.Max(Wipe.R(h * 0.0178), left - gap - bw), gy + y, bw, bh);
        }

        static void BtnShow(int left)
        {
            if (!Wipe.GameView(out int gx, out int gy, out int w, out int h))
                return;
            Rectangle r = BtnRect(gx, gy, w, h, left);
            var c = new Canvas(r.Width, r.Height);
            D.BtnPaint(c.G, r.Width, r.Height, h, "와이프아웃 " + Wipe.Files.Count + "장");
            c.End(btn.Handle, r.X, r.Y);
            if (GameActive())
                ShowWindow(btn.Handle, 4);  // SW_SHOWNOACTIVATE
        }

        // ---- 도전 결과 화면 ----
        // "완료" 글자의 흰 획 위 점과 획 사이 검은 점 (16:9에서 잰 가로 비율, 높이 비율)
        static readonly List<(double a, double y, bool white)> DONE = Pts(
            "0.8738,0.9549 0.8758,0.9542 0.8770,0.9514 0.8777,0.9611 0.8797,0.9458 0.8820,0.9563 0.8832,0.9458 0.8840,0.9486 0.8887,0.9507 0.8895,0.9590 0.8910,0.9486 0.8926,0.9486 0.8938,0.9576 0.8953,0.9493",
            "0.8703,0.9361 0.8711,0.9535 0.8723,0.9375 0.8734,0.9396 0.8766,0.9667 0.8805,0.9660 0.8844,0.9674 0.8867,0.9639 0.8898,0.9389 0.8930,0.9396 0.8961,0.9403 0.8984,0.9500 0.8992,0.9674 0.9004,0.9514");

        // 가로 비율 x는 오른쪽 끝 기준 A로 바꿔 둔다
        static List<(double, double, bool)> Pts(string white, string dark)
        {
            var o = new List<(double, double, bool)>();
            foreach (var (s, wt) in new[] { (dark, false), (white, true) })  // AutoHotkey판과 같은 순서 (검은 점 먼저)
                foreach (string xy in s.Split(' '))
                {
                    string[] p = xy.Split(',');
                    double x = double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                    o.Add(((1 - x) * 16 / 9, double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), wt));
                }
            return o;
        }

        // 도전 결과 화면: (1) 위쪽 검은 띠 + 오른쪽 아래 알약형 버튼 모양, 또는 (2) "완료" 글자.
        // 와이프아웃을 찍은 뒤에만 보므로 모양만으로 충분하다. 글자가 다르거나 버튼이 바뀐 결과 화면도 (1)로 알아본다
        public static bool ResultScreen()
        {
            if (!Wipe.GameView(out int gx, out int gy, out int w, out int h))
                return false;
            if (TopDark(gx, gy, w, h) && DoneBox(gx, gy, w, h, out _, out _, out _, out _))
                return true;
            // 흰 지점·검은 지점이 각각 85% 이상 맞으면 일치
            double[] lum = Sample(gx, gy, w, h);
            int okw = 0, okd = 0, nw = 0;
            for (int i = 0; i < DONE.Count; i++)
                if (DONE[i].white)
                {
                    nw++;
                    okw += lum[i] > 170 ? 1 : 0;
                }
                else
                    okd += lum[i] < 70 ? 1 : 0;
            return okw >= nw * 0.85 && okd >= (DONE.Count - nw) * 0.85;
        }

        // 결과 화면 위쪽(0.005~0.06h)은 검은 띠다 (전투·와이프아웃 화면은 밝다). 8px·2줄 간격으로 80% 이상이 밝기 12 미만이면 검은 띠
        static bool TopDark(int gx, int gy, int w, int h)
        {
            int y0 = Wipe.R(h * 0.005), rows = Wipe.R(h * 0.06) - y0, tot = 0, dark = 0;
            int[] buf = Wipe.GrabLum(gx, gy + y0, w, rows);
            for (int i = 0; i < rows; i += 2)
                for (int x = 0; x < w; x += 8)
                {
                    tot++;
                    if (Lum(buf[i * w + x]) < 12)
                        dark++;
                }
            return tot > 0 && dark >= tot * 0.8;  // 버프 글자·"자동 분해" 버튼이 있는 결과 화면은 0.89
        }

        // 지점들을 감싸는 영역만 한 번 캡처해서 밝기를 읽음
        static double[] Sample(int gx, int gy, int w, int h)
        {
            var xy = new List<(int x, int y)>();
            int x0 = 99999, y0 = 99999, x1 = 0, y1 = 0;
            foreach (var p in DONE)
            {
                int x = RX(w, h, p.a), y = Wipe.R(h * p.y);
                xy.Add((x, y));
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            int bw = x1 - x0 + 1;
            int[] buf = Wipe.GrabLum(gx + x0, gy + y0, bw, y1 - y0 + 1);
            var o = new double[xy.Count];
            for (int i = 0; i < xy.Count; i++)
            {
                int c = buf[(xy[i].y - y0) * bw + xy[i].x - x0];
                o[i] = ((c & 0xFF) + ((c >> 8) & 0xFF) + ((c >> 16) & 0xFF)) / 3.0;
            }
            return o;
        }

        // 검사(--resulttest): 결과 화면 판단과 버튼 자리를 화면 대신 지금 FakeScreen 그림으로 계산해 한 줄로 돌려준다
        public static string ResultInfo()
        {
            if (!Wipe.GameView(out int gx, out int gy, out int w, out int h))
                return "게임 영역 못 찾음";
            bool shape = TopDark(gx, gy, w, h) && DoneBox(gx, gy, w, h, out _, out _, out _, out _);
            bool ok = ResultScreen();
            DoneBox(gx, gy, w, h, out int dt, out int dh, out int dw, out int dg);
            int left = BtnLeft(gx, gy, w, h);
            Rectangle r = BtnRect(gx, gy, w, h, left);
            return $"게임 영역 {gx},{gy} {w}x{h} / 결과 화면 {(ok ? "예" : "아니오")} (모양 {(shape ? "예" : "아니오")}) / 완료 버튼 위 {gy + dt} 높이 {dh} 폭 {dw} 간격 {dg} / 게임 버튼 줄 왼쪽 끝 {gx + left} / 우리 버튼 {r.X},{r.Y} {r.Width}x{r.Height}";
        }

        // ---- 창 열기·닫기 ----

        static Rectangle GameArea(int minW)
        {
            if (Wipe.GameView(out int gx, out int gy, out int gw, out int gh) && gw >= minW)
                return new Rectangle(gx, gy, gw, gh);
            return new Rectangle(0, 0, ScreenW, ScreenH);  // 게임 창이 없거나 작으면 화면 기준
        }

        public static void ShowWipe()
        {
            if (Wipe.Files.Count == 0)
            {
                tray.ShowBalloonTip(3000, "ZZZ 와이프아웃 캡처", "아직 찍은 와이프아웃 장면이 없습니다", ToolTipIcon.None);
                return;
            }
            ViewClose();
            WV = new ViewWindow(GameActive());  // 게임 중이면 게임 위에 바로
            Size sz = WV.Build();
            Rectangle a = GameArea(1);  // 게임 화면 가운데에 띄운다
            WV.Open(a.X + (a.Width - sz.Width) / 2, a.Y + (a.Height - sz.Height) / 2);
        }

        public static void ViewClose()
        {
            PhotoClose();
            if (WV != null)
            {
                ReturnFocus(WV);
                WV.Dispose();
                WV = null;
            }
        }

        public static void ShowPhoto(int k)
        {
            bool overlay = WV != null ? WV.Overlay : GameActive();
            PhotoClose();
            Rectangle a = GameArea(800);
            PV = new PhotoWindow(overlay);
            Size sz = PV.Build(k, a.Width, a.Height);
            PV.Open(a.X + (a.Width - sz.Width) / 2, a.Y + (a.Height - sz.Height) / 2);
        }

        public static void PhotoClose()
        {
            if (PV != null)
            {
                ReturnFocus(PV);
                PV.Dispose();
                PV = null;
            }
        }

        public static void ShowSettings()
        {
            if (SV != null)
            {
                SetForegroundWindow(SV.Handle);
                SV.Activate();
                return;
            }
            SV = new SettingsWindow();
            Size sz = SV.Build();
            SV.Open((ScreenW - sz.Width) / 2, (ScreenH - sz.Height) / 2);
        }

        public static void SettingsClose()
        {
            if (SV != null)
            {
                SV.Dispose();
                SV = null;
            }
        }

        // ---- 저장 ----

        // i번 장면만(0이면 전체) "찍은시각_장면-순번.jpg"로 저장하고 결과 문구를 돌려준다. 사진 폴더는 Windows 보안의 제어된 폴더 액세스가
        // 쓰기를 막을 수 있어서, 막히면 사용자 폴더(C:\Users\이름\젠존제 와이프아웃)에 저장한다
        public static string Save(int i)
        {
            string err = "", msg;
            foreach (string dir in SaveDirs())
            {
                int n = 0;
                try
                {
                    Directory.CreateDirectory(dir);
                    for (int k = 0; k < Wipe.Files.Count; k++)
                        if (i == 0 || k + 1 == i)
                        {
                            string f = Wipe.Files[k];
                            File.Copy(f, Path.Combine(dir, Wipe.Stamp + "_" + Path.GetFileName(f)), true);
                            Wipe.Saved.Add(k + 1);
                            n++;
                        }
                    Wipe.SavedDir = dir;
                    Status(msg = n + "장 저장: " + dir);
                    return msg;
                }
                catch (Exception e)
                {
                    err = e.Message;
                }
            }
            Status(msg = "저장 실패: " + err);
            return msg;
        }

        static void Status(string text)
        {
            if (WV == null)
                return;
            WV.Status = text;
            WV.Draw();
        }

        // 마지막으로 저장한 폴더를, 아직 저장 전이면 저장 폴더를 연다 (없으면 만든다)
        public static void OpenDir()
        {
            if (Wipe.SavedDir != null && Directory.Exists(Wipe.SavedDir))
            {
                Explore(Wipe.SavedDir);
                return;
            }
            foreach (string dir in SaveDirs())
                if (Directory.Exists(dir))
                {
                    Explore(dir);
                    return;
                }
            foreach (string dir in SaveDirs())
                try
                {
                    Directory.CreateDirectory(dir);
                    Explore(dir);
                    return;
                }
                catch { }
        }

        static void Explore(string dir)
        {
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        // 저장할 폴더 순서: 설정한 폴더(없으면 사진\젠존제 와이프아웃), 막히면 사용자 폴더\젠존제 와이프아웃
        public static string[] SaveDirs()
        {
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (pics == "")
                pics = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return new[]
            {
                Cfg.Dir != "" ? Cfg.Dir : Path.Combine(pics, "젠존제 와이프아웃"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "젠존제 와이프아웃"),
            };
        }
    }
}
