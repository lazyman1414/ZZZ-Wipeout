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
            List<Rectangle> row = null;
            if (Wipe.GameView(out int gx, out int gy, out int w, out int h))
                row = ButtonRow(gx, gy, w, h);
            if (row != null)
            {
                away = 0;
                Wipe.Seen = true;
                if (IsWindowVisible(btn.Handle))
                    return;
                // 버튼 줄이 다 나타날 때까지 기다린다: 가장 왼쪽 끝이 0.1초 간격으로 세 번 같으면 그 왼쪽에 띄운다
                int left = BtnLeft(gx, gy, w, h, row);
                same = Math.Abs(left - lastLeft) <= h * 0.009 ? same + 1 : 0;
                lastLeft = left;
                if (same >= 2)
                    BtnShow(BtnRect(gx, gy, h, left, row), h);
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

        // ---- 도전 결과 화면 ----
        // 결과 화면 아래에는 알약 모양 게임 버튼("한 번 더", "완료" 등)이 같은 높이로 나란히 놓인다. 버튼은 검은 바탕 위에 밝은 테두리
        // (밝기 40~77)이고 속은 어둡다(0~18). 위치는 가정하지 않는다: 모니터 비율, 검은 띠를 없애는 모드 등에 따라 버튼 줄이
        // 오른쪽 끝에 붙기도 하고 가운데 16:9 안에 있기도 해서, 화면 아래쪽 전체에서 버튼 모양을 직접 찾는다.
        // 크기는 화면 높이(h)에 비례한다 (16:9 결과 화면에서 잰 값: 버튼 높이 0.0533h, 완료 버튼 폭 0.2836h, 버튼 간격 0.0204h)

        static int Lum(int c) => (((c >> 16) & 0xFF) * 77 + ((c >> 8) & 0xFF) * 150 + (c & 0xFF) * 29) >> 8;

        // 화면 아래쪽(0.86h~)의 게임 버튼 줄(게임 영역 안 좌표, 왼쪽부터). 같은 줄에 같은 높이로 게임 간격만큼 떨어진 버튼이 2개 이상이고
        // 가장 오른쪽 버튼이 완료 버튼 크기일 때만 돌려준다. 없으면 null
        static string Dbg = "";
        static List<Rectangle> ButtonRow(int gx, int gy, int w, int h)
        {
            int y0 = Wipe.R(h * 0.86), rows = h - y0;
            int[] buf = Wipe.GrabLum(gx, gy + y0, w, rows);
            var L = new byte[buf.Length];
            for (int i = 0; i < buf.Length; i++)
                L[i] = (byte)Lum(buf[i]);
            bool Lit(int x, int y) => L[y * w + x] > 28;
            // 1) 테두리의 곧은 위아래 선: 밝은 점이 0.15~0.5h 이어지는 가로 줄 (더 긴 줄은 버튼이 아닌 배경·패널)
            int minRun = Wipe.R(h * 0.15), maxRun = Wipe.R(h * 0.5), tol = Math.Max(2, Wipe.R(h * 0.006));
            var runs = new List<(int y, int x1, int x2)>();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Lit(x, y))
                        continue;
                    int s = x;
                    while (x < w && Lit(x, y)) x++;
                    if (x - s >= minRun && x - s <= maxRun)
                        runs.Add((y, s, x - 1));
                }
            // 2) 알약 버튼: 위 선 아래로 버튼 높이(0.045~0.062h)만큼 떨어져 시작·끝이 같은 아래 선이 있고, 가운데 줄에서 곧은 선이 시작하는 곳은
            //    속(어두움)이며, 둥근 양 끝까지 잰 폭이 곧은 선 길이 + 높이와 맞는 것
            var btns = new List<Rectangle>();
            int hMin = (int)Math.Ceiling(h * 0.045), hMax = (int)(h * 0.062);
            foreach (var a in runs)
            {
                int bestB = -1;
                foreach (var b in runs)
                    if (b.y - a.y + 1 >= hMin && b.y - a.y + 1 <= hMax && Math.Abs(a.x1 - b.x1) <= tol && Math.Abs(a.x2 - b.x2) <= tol)
                        bestB = Math.Max(bestB, b.y);
                if (bestB < 0)
                    continue;
                int m = (a.y + bestB) / 2, bh = bestB - a.y + 1;
                if (Lit(a.x1 + tol, m) || Lit(a.x2 - tol, m))
                    continue;
                int Edge(int from, int dir)  // from(버튼 속, 글자보다 바깥)에서 dir 쪽으로 속을 지나 테두리 끝까지
                {
                    int x = from;
                    while (x > 0 && x < w - 1 && !Lit(x, m)) x += dir;
                    while (x > 0 && x < w - 1 && Lit(x + dir, m)) x += dir;
                    return x;
                }
                int l = Edge(a.x1 + tol, -1), r = Edge(a.x2 - tol, 1);
                if (Math.Abs((r - l + 1) - (a.x2 - a.x1 + 1 + bh)) > bh / 2)
                    continue;
                var rc = new Rectangle(l, y0 + a.y, r - l + 1, bh);
                // 테두리가 2~3px 두께라 같은 버튼이 여러 번 잡힌다: 가로로 겹치면 더 큰 쪽만
                int k = btns.FindIndex(o => o.Left <= rc.Right && rc.Left <= o.Right);
                if (k < 0)
                    btns.Add(rc);
                else if (rc.Height > btns[k].Height)
                    btns[k] = rc;
            }
            Dbg = $"선 {runs.Count}개, 버튼 " + string.Join(" ", btns.Select(o => $"[{o.Left},{o.Top} {o.Width}x{o.Height}]"));
            // 3) 가장 오른쪽 버튼과 같은 줄(위 끝·높이가 2px 안)에 있는 버튼들
            if (btns.Count < 2)
                return null;
            btns.Sort((p, q) => p.Left.CompareTo(q.Left));
            Rectangle done = btns[btns.Count - 1];
            var row = btns.Where(o => Math.Abs(o.Top - done.Top) <= 2 && Math.Abs(o.Height - done.Height) <= 2).ToList();
            if (row.Count < 2 || done.Width < h * 0.267 || done.Width > h * 0.302)
                return null;
            int gap = done.Left - row[row.Count - 2].Right - 1;
            return gap >= h * 0.009 && gap <= h * 0.036 ? row : null;
        }

        // 결과 화면 아래 줄에서 게임이 그린 것(버튼, "한 번 더" 왼쪽에 붙는 배터리 칸 등) 중 가장 왼쪽 끝(게임 영역 안 x)을 돌려준다.
        // 결과마다 왼쪽에 붙는 것이 달라서 매번 잰다. 버튼 줄 높이를 세로로 훑은 최댓값이 20을 넘으면 무언가 있는 칸으로 보고,
        // 버튼 줄의 가장 왼쪽 버튼에서 왼쪽으로 1px씩 따라가다 빈 칸이 0.053h보다 길게 이어지면(버튼 간격은 0.020h) 멈춘다
        static int BtnLeft(int gx, int gy, int w, int h, List<Rectangle> row)
        {
            Rectangle b = row[0];
            int[] buf = Wipe.GrabLum(gx, gy + b.Top, w, b.Height);
            int left = b.Left, dark = 0, gapMax = Wipe.R(h * 0.0533);
            for (int x = left - 1; x > 0; x--)
            {
                int m = 0;
                for (int i = 0; i < b.Height; i++)
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

        // 결과 화면 버튼의 자리(화면 좌표): 게임 버튼 줄의 가장 왼쪽(left, 게임 영역 안 x) 바로 왼쪽에 완료 버튼과 같은 크기·같은 줄·같은 간격
        public static Rectangle BtnRect(int gx, int gy, int h, int left, List<Rectangle> row)
        {
            Rectangle done = row[row.Count - 1];
            int gap = done.Left - row[row.Count - 2].Right - 1;
            return new Rectangle(gx + Math.Max(Wipe.R(h * 0.0178), left - gap - done.Width), gy + done.Top, done.Width, done.Height);
        }

        static void BtnShow(Rectangle r, int h)
        {
            var c = new Canvas(r.Width, r.Height);
            D.BtnPaint(c.G, r.Width, r.Height, h, "와이프아웃 " + Wipe.Files.Count + "장");
            c.End(btn.Handle, r.X, r.Y);
            if (GameActive())
                ShowWindow(btn.Handle, 4);  // SW_SHOWNOACTIVATE
        }

        public static bool ResultScreen() => Wipe.GameView(out int gx, out int gy, out int w, out int h) && ButtonRow(gx, gy, w, h) != null;

        // 검사(--resulttest): 결과 화면 판단과 버튼 자리를 화면 대신 지금 FakeScreen 그림으로 계산해 한 줄로 돌려준다
        public static string ResultInfo()
        {
            if (!Wipe.GameView(out int gx, out int gy, out int w, out int h))
                return "게임 영역 못 찾음";
            var row = ButtonRow(gx, gy, w, h);
            if (row == null)
                return $"게임 영역 {gx},{gy} {w}x{h} / 결과 화면 아니오 / {Dbg}";
            Rectangle done = row[row.Count - 1];
            int left = BtnLeft(gx, gy, w, h, row);
            Rectangle r = BtnRect(gx, gy, h, left, row);
            return $"게임 영역 {gx},{gy} {w}x{h} / 결과 화면 예 / 버튼 {row.Count}개, 완료 버튼 {gx + done.Left},{gy + done.Top} {done.Width}x{done.Height} 간격 {done.Left - row[row.Count - 2].Right - 1} / 게임 버튼 줄 왼쪽 끝 {gx + left} / 우리 버튼 {r.X},{r.Y} {r.Width}x{r.Height}";
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
