using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static ZzzWipeout.Native;

namespace ZzzWipeout
{
    // 원본 크기로 찍은 게임 화면 (32비트 top-down DIB)
    sealed class Frame
    {
        public IntPtr Hbm, Bits;
        public int W, H;
        public int Src;  // 재생 검사에서 몇 번째 프레임인지

        public Frame(int w, int h)
        {
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            Hbm = NewDib(mdc, w, h, out Bits);
            DeleteDC(mdc);
            ReleaseDC(IntPtr.Zero, sdc);
            W = w; H = h;
        }

        public void Free()
        {
            if (Hbm != IntPtr.Zero)
                DeleteObject(Hbm);
            Hbm = Bits = IntPtr.Zero;
        }
    }

    sealed class Shot { public Frame F; public double Score; }

    sealed class Scene
    {
        public List<Shot> Shots = new List<Shot>();
        public bool Keep, Stab;
        public int Cnt;
        public double MaxSat, Med, Best;
        public List<double> Jags = new List<double>();
        public StringBuilder Tears = new StringBuilder();
        public byte[] Sig;
    }

    sealed class Stats { public byte[] L; public double Yel, Sat, Tear, Diff; }

    // 와이프아웃 캡처: 마지막 적을 쓰러뜨리면 화면이 노랗게 바뀌고 WIPEOUT 글자와 함께 장면 몇 개(보통 3개)가
    // 잠깐씩 멈춘다. 장면 사이엔 화면이 회색으로 번쩍이고, 멈춘 장면도 뒤로 갈수록 가로로 찢어지는 지지직이 생긴다.
    // 화면을 128x72로 줄여(평균) 화면의 40% 이상이 노랗고 채도가 높고 직전과 거의 같은 순간이 두 번 이어지면 멈춘 장면으로 보고,
    // 같은 장면 중 지글거림·가로줄이 가장 적은 순간을 원본 크기로 남긴다. 기준값은 2560x1440 영상에서 뽑음.
    // 끝나면 임시 폴더에 저장하고, 도전 결과 화면에 확인 버튼을 띄운다
    static class Wipe
    {
        public static bool On, Ready, Seen;
        public static List<string> Files = new List<string>();
        public static HashSet<int> Saved = new HashSet<int>();  // 저장 폴더에 저장한 사진 번호 (1부터)
        public static List<List<int>> Groups = new List<List<int>>();  // 장면마다 Files 번호(1부터)
        public static string Dir = Path.Combine(Path.GetTempPath(), "ZZZ-Wipeout");
        public static string LogFile = Path.Combine(Path.GetTempPath(), "ZZZ-Wipeout.log");

        // 검사(Replay.cs, ResultTest): 화면 대신 이 그림을 읽는다 (게임 창 = 그림 전체). 재생 검사는 시계도 프레임 간격으로 돌린다
        public static Frame FakeScreen;
        public static bool Replaying;
        public static int ReplayNow;
        public static Action<Frame, string> ReplaySaved;
        public const int W = 128, H = 72;
        public static double Ratio = 9.0 / 16;
        public static string Stamp = "", Note = "", SavedDir;
        public static int EndTick;

        static byte[] prev;
        static bool okPrev;
        static List<Scene> scenes = new List<Scene>();
        static Frame work;
        static int start, lastYel, ticks;
        static double ms;
        static StringBuilder trace = new StringBuilder();
        static List<(Frame f, int tick)> dbg = new List<(Frame, int)>();
        static StringBuilder dbgCsv = new StringBuilder();
        static Timer timer;

        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public static int R(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
        static int Now => Replaying ? ReplayNow : Environment.TickCount;

        // 주황~노랑(색상 약 25~55도)이고 채도가 높으면 노란색. 맵이 어두우면 노란 화면도 어두워서 밝기는 낮게(R>90) 본다
        static bool Yellow(uint c)
        {
            int r = (int)(c >> 16) & 0xFF;
            if (r <= 90 || !(r - (int)(c & 0xFF) > r * 0.45))
                return false;
            int g = (int)(c >> 8) & 0xFF;
            return g > r * 0.45 && g < r * 0.95;
        }

        // 0.1초마다: 화면 위쪽(0.06h) 한 줄의 64곳 중 절반 이상이 노란색이면 와이프아웃 시작으로 본다.
        // 4분의 1 이상이면 판별 기준을 맞출 수 있게 색을 로그에 남긴다
        static int topLogged;
        public static bool Top(bool cursor)
        {
            if (!GameView(out int gx, out int gy, out int w, out int h))
                return false;
            return TopRow(GrabLum(gx, gy + R(h * 0.06), w, 1), w, cursor);
        }

        // 완전한 검정 점(게임 영역을 아직 모를 때 들어온 화면 비율 차이의 검은 띠)은 빼고, 남은 점의 절반 이상이 노란지 본다
        public static bool TopRow(int[] buf, int w, bool cursor)
        {
            int n = 0, lit = 0;
            for (int i = 1; i <= 64; i++)
            {
                int c = buf[(int)Math.Floor(w * (i - 0.5) / 64)];
                if ((c & 0xFFFFFF) == 0)
                    continue;
                lit++;
                n += Yellow((uint)c) ? 1 : 0;
            }
            if (n >= 16 && Now - topLogged > 500)
            {
                topLogged = Now;
                var cols = new StringBuilder();
                for (int i = 1; i <= 8; i++)
                    cols.Append(" " + (buf[(int)Math.Floor(w * (i - 0.5) / 8)] & 0xFFFFFF).ToString("X6"));
                Log($"위쪽 노란색 {n}/{lit}, 커서 {(cursor ? "보임" : "숨김")}, 색{cols}");
            }
            return lit >= 16 && n * 2 >= lit;
        }

        // 같은 내용은 연달아 남기지 않는다 (0.1초마다 같은 오류가 반복되는 경우)
        static string lastLog = "";
        public static void Log(string s)
        {
            if (s == lastLog)
                return;
            lastLog = s;
            try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n", new UTF8Encoding(false)); } catch { }
        }

        public static void Start()
        {
            Log("와이프아웃 시작");
            On = true; prev = null; okPrev = false; scenes = new List<Scene>(); work = null;
            start = lastYel = Now; ticks = 0; ms = 0; trace.Clear();
            dbg.Clear(); dbgCsv.Clear().Append("판별,시각ms,노랑,채도,차이,찢어짐,줄무늬,지글거림,상태,장면\n");
            if (Replaying)
                return;
            if (timer == null)
            {
                timer = new Timer { Interval = 30 };
                timer.Tick += (s, e) => { timer.Stop(); Tick(); if (On) timer.Start(); };
            }
            timer.Start();  // 판별이 끝난 뒤부터 30ms를 잰다 (AutoHotkey판의 실제 간격 40~45ms와 같게)
        }

        // 매번 원본 크기로 한 번 찍고 그 그림을 메모리에서 줄여 판별한다. 후보로 고르면 판별한 그 그림을 그대로 남긴다
        // (화면에서 바로 줄이면 느리고, 후보를 다시 찍으면 그사이 화면이 바뀌어 짧은 장면을 놓쳤음)
        public static void Tick()
        {
            try { TickCore(); }
            catch (Exception e) { Log("오류: " + e.Message); End(); }
        }

        static void TickCore()
        {
            long t0 = Stopwatch.GetTimestamp();
            if (!GameView(out int gx, out int gy, out int w, out int h) || w <= 0 || h <= 0)
            {
                End();
                return;
            }
            work = GrabFull(gx, gy, w, h, work);  // 게임이 그리는 영역만 (화면 비율이 달라 생긴 검은 띠는 빼고)
            Stats s = GetStats(Shrink(work), prev);
            prev = s.L;
            // 노랗고 채도가 높고 직전과 크게 다르지 않은(지지직·번쩍임은 차이 13 이상) 화면은 모두 장면 후보로 묶는다.
            // 노랑 기준 0.35: 바닥이 회색인 맵은 실제 장면도 노란 부분이 0.38에 그친다 (진단 자료)
            // 그중 직전과 거의 같은(차이 5 미만, 머리카락 등이 조금 움직임) 화면이 두 번 이어지면 멈춘 장면
            bool cand = s.Yel >= 0.35 && s.Sat >= 60 && s.Diff < 12;
            // 마지막 장면은 검은 화면과 번갈아 깜빡이며 끝나서 직전 화면과는 늘 크게 다르다. 그래도 그 장면과 비슷하면 후보로 넣는다
            if (!cand && s.Yel >= 0.35 && s.Sat >= 60 && scenes.Count > 0 && Diff(s.L, scenes[scenes.Count - 1].Sig) < 15)
                cand = true;
            bool ok = cand && s.Diff < 5, stable = ok && okPrev;
            char flag = stable ? 'S' : cand ? 'c' : s.Yel >= 0.35 ? 'y' : '.';
            trace.Append(flag);
            double scan = double.NaN, jag = double.NaN;
            if (Cfg.Debug)  // 진단: 화면 전체를 절반 크기로 남긴다 (끝난 뒤 저장)
                DebugKeep(w, h);
            if (cand)
            {
                Scene sc = scenes.Count > 0 ? scenes[scenes.Count - 1] : null;
                if ((sc == null || Diff(s.L, sc.Sig) > 15) && scenes.Count < 12)  // 마지막 장면과 많이 다르면 새 장면
                    scenes.Add(sc = new Scene());
                // 점수(낮을수록 깨끗함) = 지글거림(jag) + 색 빠짐 벌점(채도 100 미만) + 가로 줄무늬 조금 + 멈춤이 아니면 아주 조금
                jag = Jag(work); scan = Scan(work);
                double score = jag + Math.Max(0, 100 - s.Sat) * 0.004 + scan * 0.002 + (stable ? 0 : 0.01);
                sc.Sig = s.L; sc.Cnt++; sc.Stab = sc.Stab || stable; sc.MaxSat = Math.Max(sc.MaxSat, s.Sat); sc.Jags.Add(jag);
                sc.Tears.Append(" " + (stable ? "" : "~") + score.ToString("F3", IC));
                // 장면마다 점수가 좋은 장면당 캡처 수(설정, 기본 3)만큼 보관한다. 꽉 차면 가장 나쁜 것과 바꾸고, 밀려난 그림은 다음 판별에 다시 쓴다
                if (sc.Shots.Count < Cfg.Per)
                {
                    sc.Shots.Add(new Shot { F = work, Score = score });
                    work = null;
                    Ratio = (double)h / w;
                }
                else
                {
                    int wi = 0;
                    for (int i = 0; i < sc.Shots.Count; i++)
                        if (sc.Shots[i].Score > sc.Shots[wi].Score)
                            wi = i;
                    if (score < sc.Shots[wi].Score)
                    {
                        Frame t = sc.Shots[wi].F;
                        sc.Shots[wi] = new Shot { F = work, Score = score };
                        work = t;
                    }
                }
            }
            okPrev = ok;
            if (Cfg.Debug)
                dbgCsv.Append(string.Format(IC, "{0},{1},{2:F2},{3:F0},{4:F1},{5:F3},{6},{7},{8},{9}\n", ticks, Now - start, s.Yel, s.Sat, s.Diff, s.Tear
                    , double.IsNaN(scan) ? "" : Math.Round(scan, 1).ToString(IC), double.IsNaN(jag) ? "" : Math.Round(jag, 3).ToString(IC), flag, cand ? scenes.Count.ToString() : ""));
            if (s.Yel >= 0.2)
                lastYel = Now;
            ticks++;
            ms += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            // 노란 화면이 1초 넘게 안 보이면(검은 화면·결과 화면) 끝. 시작 직후 전환 효과는 노란 부분이 적어서 1.5초까지는 기다린다.
            // 노란 맵을 잘못 본 경우를 위해 15초에서도 끝
            if (Now - lastYel > 1000 && Now - start > 1500 || Now - start > 15000)
                End();
        }

        // 장면 사이엔 시간이 없어서 원본은 메모리에 두었다가 끝난 뒤에 저장한다
        static void End()
        {
            timer?.Stop();
            On = false;
            work?.Free();
            work = null;
            if (Cfg.Debug)
                DebugSave();
            // 각 장면 앞에는 캐릭터가 회색으로 바랜 전환 화면이 잠깐 나온다(진단 자료: 화면 채도 72~80, 실제 장면은 102~141).
            // 그래서 후보 2번 이상이고 채도가 90 이상까지 올라간 장면을 모두 남긴다. 2개가 안 되면 나머지 장면 중 점수가 좋은 것으로 채운다
            var rest = new List<Scene>();
            int need = 2;
            foreach (Scene sc in scenes)
            {
                sc.Med = Median(sc.Jags);
                sc.Best = 9;
                foreach (Shot sh in sc.Shots)
                    sc.Best = Math.Min(sc.Best, sh.Score);
                // 회색 전환 화면은 후보 1~2번에 멈춤이 없다. 실제 장면이라도 맵에 따라 채도가 79에 그칠 때가 있어서
                // 채도가 90 이상이거나, 멈춤이 있었거나, 후보가 3번 이상이면 남긴다
                if (sc.Cnt >= 2 && (sc.MaxSat >= 90 || sc.Stab || sc.Cnt >= 3))
                {
                    sc.Keep = true;
                    need--;
                }
                else if (sc.Shots.Count > 0)
                    rest.Add(sc);
            }
            for (int n = 0; n < Math.Max(0, need); n++)
            {
                int bi = -1;
                for (int i = 0; i < rest.Count; i++)
                    if (bi < 0 || rest[i].Best < rest[bi].Best)
                        bi = i;
                if (bi >= 0)
                {
                    rest[bi].Keep = true;
                    rest.RemoveAt(bi);
                }
            }
            var groups = new List<List<Shot>>();
            int total = 0;
            foreach (Scene sc in scenes)
            {
                if (sc.Keep)  // 점수가 좋은 순서로 (같은 점수는 먼저 찍힌 순서 그대로)
                {
                    var g = sc.Shots.ToList();
                    for (int i = 1; i < g.Count; i++)
                        for (int j = 0; j < g.Count - i; j++)
                            if (g[j].Score > g[j + 1].Score)
                                (g[j], g[j + 1]) = (g[j + 1], g[j]);
                    groups.Add(g);
                    total += g.Count;
                }
                else
                    foreach (Shot sh in sc.Shots)
                        sh.F.Free();
            }
            var log = new StringBuilder(string.Format(IC, "{0} 와이프아웃 {1:F1}초, 판별 {2}회 평균 {3:F1}ms, 장면 {4}개 중 {5}개 {6}장 저장\n  판별: {7}\n"
                , DateTime.Now.ToString("HH:mm:ss"), (Now - start) / 1000.0, ticks, ms / Math.Max(ticks, 1), scenes.Count, groups.Count, total, trace));
            for (int i = 0; i < scenes.Count; i++)
            {
                Scene sc = scenes[i];
                log.Append(string.Format(IC, "  장면 {0}{1}: 최대 채도 {2:F0}, 지글거림 중앙값 {3:F3}, 가장 좋은 점수 {4:F3} / 후보(~는 멈춤 아님){5}\n"
                    , i + 1, sc.Keep ? "" : " (버림)", sc.MaxSat, sc.Med, sc.Best, sc.Tears));
            }
            try { File.AppendAllText(LogFile, log.ToString(), new UTF8Encoding(false)); } catch { }
            if (groups.Count == 0)  // 하나도 못 찍었으면 이전 캡처를 그대로 둔다
            {
                scenes = new List<Scene>();
                return;
            }
            App.ViewClose();
            try { Directory.Delete(Dir, true); } catch { }
            Directory.CreateDirectory(Dir);
            var files = new List<string>();
            Groups = new List<List<int>>();
            for (int gi = 0; gi < groups.Count; gi++)  // 파일 이름: 장면-순번 (1-1이 그 장면에서 가장 깨끗함)
            {
                var row = new List<int>();
                for (int j = 0; j < groups[gi].Count; j++)
                {
                    Shot sh = groups[gi][j];
                    string f = Path.Combine(Dir, $"{gi + 1}-{j + 1}{(Cfg.Png ? ".png" : ".jpg")}");
                    ReplaySaved?.Invoke(sh.F, f);
                    if (SaveImage(sh.F, f))
                    {
                        files.Add(f);
                        row.Add(files.Count);
                    }
                    sh.F.Free();
                }
                if (row.Count > 0)
                    Groups.Add(row);
            }
            scenes = new List<Scene>();
            prev = null;
            Files = files;
            Saved = new HashSet<int>();
            Stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            EndTick = Now;
            Ready = files.Count > 0;
            Seen = false;
            Note = "";
            if (Cfg.Auto && files.Count > 0)  // 자동 저장: 확인 창을 열면 결과 문구가 보인다
            {
                Note = "자동 " + App.Save(0);
                Log(Note);
            }
        }

        // 진단 모드(설정.ini의 디버그=1): 판별한 화면마다 전체를 절반 크기(2560x1440이면 1280x720)로 보관한다.
        // 노랗지 않아 놓친 장면도 볼 수 있게 노란 화면만이 아니라 모두 남긴다 (최대 120장)
        static void DebugKeep(int w, int h)
        {
            if (dbg.Count >= 120)
                return;
            var f = new Frame(w / 2, h / 2);
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr src = CreateCompatibleDC(sdc), so = SelectObject(src, work.Hbm);
            IntPtr dst = CreateCompatibleDC(sdc), d0 = SelectObject(dst, f.Hbm);
            SetStretchBltMode(dst, 4);
            SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
            StretchBlt(dst, 0, 0, f.W, f.H, src, 0, 0, w, h, SRCCOPY);
            SelectObject(dst, d0); DeleteDC(dst);
            SelectObject(src, so); DeleteDC(src);
            ReleaseDC(IntPtr.Zero, sdc);
            dbg.Add((f, ticks));
        }

        // 진단 자료를 %TEMP%\ZZZ-Wipeout_진단에 저장한다: 판별 번호.png와 판별마다의 수치(수치.csv)
        static void DebugSave()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ZZZ-Wipeout_진단");
            try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            try { File.WriteAllText(Path.Combine(dir, "수치.csv"), dbgCsv.ToString(), new UTF8Encoding(true)); } catch { }
            foreach (var d in dbg)
            {
                SaveImage(d.f, Path.Combine(dir, d.tick.ToString("000") + ".png"));
                d.f.Free();
            }
            dbg.Clear();
        }

        // 찍어 둔 원본 그림을 128x72로 평균 내어 줄인다 (HALFTONE. 메모리 안에서라 2560x1440도 약 5ms)
        static int[] Shrink(Frame f)
        {
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr src = CreateCompatibleDC(sdc), sold = SelectObject(src, f.Hbm);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr dib = NewDib(mdc, W, H, out IntPtr bits);
            IntPtr old = SelectObject(mdc, dib);
            SetStretchBltMode(mdc, 4);
            SetBrushOrgEx(mdc, 0, 0, IntPtr.Zero);
            StretchBlt(mdc, 0, 0, W, H, src, 0, 0, f.W, f.H, SRCCOPY);
            GdiFlush();
            var buf = new int[W * H];
            Marshal.Copy(bits, buf, 0, buf.Length);
            SelectObject(mdc, old); DeleteObject(dib); DeleteDC(mdc);
            SelectObject(src, sold); DeleteDC(src);
            ReleaseDC(IntPtr.Zero, sdc);
            return buf;
        }

        // 줄인 화면의 밝기(L), 화면 전체 노란 비율(위쪽이 흰 건물인 장면도 있어 전체를 봄), 평균 채도, 세로/가로 변화량 비(tear), 직전 밝기와의 평균 차이(diff)
        static Stats GetStats(int[] buf, byte[] prev)
        {
            var L = new byte[W * H];
            double yel = 0, sat = 0, v = 0, hz = 0, diff = 0;
            int k = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++, k++)
                {
                    uint c = (uint)buf[k];
                    int r = (int)(c >> 16) & 0xFF, g = (int)(c >> 8) & 0xFF, b = (int)c & 0xFF;
                    int lum = (r * 77 + g * 150 + b * 29) >> 8;
                    L[k] = (byte)lum;
                    sat += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    if (Yellow(c))
                        yel++;
                    if (x > 0)
                        hz += Math.Abs(lum - L[k - 1]);
                    if (y > 0)
                        v += Math.Abs(lum - L[k - W]);
                    if (prev != null)
                        diff += Math.Abs(lum - prev[k]);
                }
            return new Stats { L = L, Yel = yel / (W * H), Sat = sat / (W * H), Tear = v / Math.Max(hz, 1), Diff = prev != null ? diff / (W * H) : 99 };
        }

        // 원본 그림의 가로 줄무늬(스캔라인) 세기: 64x40 지점에서 초록 값의 위아래 2차 차이(2p - 위 - 아래) 평균.
        // 와이프아웃 화면엔 한 줄 간격 줄무늬가 깔리고 세기가 순간마다 달라서 같은 장면 중 가장 옅은 순간을 고르는 데 쓴다
        static unsafe double Scan(Frame f)
        {
            byte* bits = (byte*)f.Bits;
            int w = f.W, h = f.H, stride = w * 4;
            double s = 0;
            for (int i = 1; i <= 40; i++)
            {
                byte* row = bits + (int)Math.Floor(h * (i - 0.5) / 40) * stride;
                for (int j = 1; j <= 64; j++)
                {
                    byte* p = row + (int)Math.Floor(w * (j - 0.5) / 64) * 4;
                    s += Math.Abs(2 * p[1] - p[1 - stride] - p[1 + stride]);
                }
            }
            return s / 2560;
        }

        // 캐릭터 가장자리 지글거림: 화면 가운데 절반에서 일정 간격(2560 폭이면 8px)마다, 가로로 2px 옆과 밝기가 크게 다른 곳(세로 가장자리)만 골라
        // 위아래 2줄 차이 / 가로 차이의 비를 낸다. 깨끗한 가장자리는 위아래로 이어져서 작고, 줄마다 옆으로 밀리면 커진다.
        // 2줄 차이를 써서 한 줄 간격 가로 줄무늬는 상쇄된다 (진단 자료: 깨끗함 0.35~0.44, 지글거림 0.6~0.8)
        static unsafe double Jag(Frame f)
        {
            byte* bits = (byte*)f.Bits;
            int w = f.W, h = f.H, stride = w * 4, st = Math.Max(4, w / 320);
            double ev = 0, eh = 0;
            for (int y = h / 4; y < h * 3 / 4; y += st)
            {
                byte* row = bits + y * stride;
                for (int x = w / 4; x < w * 3 / 4; x += st)
                {
                    byte* p = row + x * 4;
                    int a = p[1], dh = Math.Abs(a - p[9]);
                    if (dh > 24)
                    {
                        eh += dh;
                        ev += Math.Abs(a - p[2 * stride + 1]);
                    }
                }
            }
            return eh != 0 ? ev / eh : 0;
        }

        static double Median(List<double> a)
        {
            if (a.Count == 0)
                return 9;
            var b = a.OrderBy(v => v).ToList();
            int n = b.Count;
            return n % 2 == 1 ? b[n / 2] : (b[n / 2 - 1] + b[n / 2]) / 2;
        }

        static double Diff(byte[] a, byte[] b)
        {
            double d = 0;
            for (int i = 0; i < a.Length; i++)
                d += Math.Abs(a[i] - b[i]);
            return d / a.Length;
        }

        // 게임 화면을 원본 크기로 찍는다. f를 주면(크기가 같으면) 그 그림에 다시 찍는다
        static Frame GrabFull(int x, int y, int w, int h, Frame f)
        {
            if (f != null && (f.W != w || f.H != h))
            {
                f.Free();
                f = null;
            }
            f = f ?? new Frame(w, h);
            if (FakeScreen != null)
            {
                FakeCopy(x, y, w, h, f.Bits);
                f.Src = FakeScreen.Src;
                return f;
            }
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr old = SelectObject(mdc, f.Hbm);
            BitBlt(mdc, 0, 0, w, h, sdc, x, y, SRCCOPY);
            GdiFlush();
            SelectObject(mdc, old);
            DeleteDC(mdc);
            ReleaseDC(IntPtr.Zero, sdc);
            return f;
        }

        // 화면 일부를 찍어 점의 색을 읽을 수 있는 배열로 돌려준다 (0xAARRGGBB)
        public static int[] GrabLum(int x, int y, int cw, int ch)
        {
            if (FakeScreen != null)
            {
                var o = new int[cw * ch];
                var h = GCHandle.Alloc(o, GCHandleType.Pinned);
                try { FakeCopy(x, y, cw, ch, h.AddrOfPinnedObject()); }
                finally { h.Free(); }
                return o;
            }
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr dib = NewDib(mdc, cw, ch, out IntPtr bits);
            IntPtr old = SelectObject(mdc, dib);
            BitBlt(mdc, 0, 0, cw, ch, sdc, x, y, SRCCOPY);
            GdiFlush();
            var buf = new int[cw * ch];
            Marshal.Copy(bits, buf, 0, buf.Length);
            SelectObject(mdc, old); DeleteObject(dib); DeleteDC(mdc);
            ReleaseDC(IntPtr.Zero, sdc);
            return buf;
        }

        // 검사용 그림(FakeScreen)에서 (x, y)부터 w x h를 dst로 옮긴다 (그림 밖은 검정)
        static unsafe void FakeCopy(int x, int y, int w, int h, IntPtr dst)
        {
            Frame s = FakeScreen;
            int* d = (int*)dst, src = (int*)s.Bits;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int sx = x + i, sy = y + j;
                    d[j * w + i] = sx >= 0 && sy >= 0 && sx < s.W && sy < s.H ? src[sy * s.W + sx] : 0;
                }
        }

        // 게임이 실제로 그리는 영역(화면 좌표). 모니터 비율이 게임과 다르면(21:9, 32:9, 16:10 등) 게임은 양옆이나 위아래를
        // 완전한 검정으로 비우고 그 안에 그린다. 그 띠를 빼고 남은 곳을 모든 판별·캡처의 기준으로 쓴다.
        // 결과 화면처럼 안쪽에도 검은 띠가 있는 화면이 있어서 한 장으로 정하지 않고, 게임 창 크기마다 지금까지 검정이 아닌 것이
        // 한 번이라도 보인 가장 바깥 범위를 기억한다 (넓어지기만 하고, 전투 화면을 거치며 금방 실제 영역까지 넓어진다).
        // 화면 비율은 게임 창 크기가 바뀔 때만 달라지므로, 값이 정해지면(10번 연속 그대로이거나 창 전체) 창 크기가 바뀔 때까지 다시 재지 않는다.
        // 재는 데 화면을 6번 찍어서(2560x1440에서 약 39ms) 정해지기 전에도 1초에 한 번만, 와이프아웃을 찍는 동안에는 재지 않는다
        // (판별마다 재면 판별 간격이 두 배로 늘어 짧은 장면을 놓쳤다)
        static int viewW, viewH, vl, vt, vr, vb, viewAt, viewSame;
        public static bool GameView(out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            if (!GameClient(out int gx, out int gy, out int cw, out int ch) || cw <= 0 || ch <= 0)
                return false;
            bool sample = FakeScreen != null || viewSame < 10 && !On && unchecked(Environment.TickCount - viewAt) >= 1000;
            if (cw != viewW || ch != viewH)
            {
                viewW = cw; viewH = ch;
                vl = cw; vt = ch; vr = -1; vb = -1;
                viewSame = 0;
                sample = true;
            }
            if (sample)
                Measure(gx, gy, cw, ch);
            if (vr < vl || vb < vt || vr - vl < cw / 4 || vb - vt < ch / 4)  // 아직 모름(검은 화면 등): 창 전체
            {
                x = gx; y = gy; w = cw; h = ch;
                return true;
            }
            x = gx + vl; y = gy + vt; w = vr - vl + 1; h = vb - vt + 1;
            return true;
        }

        static void Measure(int gx, int gy, int cw, int ch)
        {
            viewAt = Environment.TickCount;
            var before = (vl, vt, vr, vb);
            Extents(gx, gy, cw, ch);
            if (vl == 0 && vt == 0 && vr == cw - 1 && vb == ch - 1)
                viewSame = 10;  // 창 전체: 더 넓어질 수 없다
            else if (vr >= vl && (vl, vt, vr, vb) == before)
                viewSame++;
            else
                viewSame = 0;
        }

        static void Extents(int gx, int gy, int cw, int ch)
        {
            bool Lit(int c) => (c & 0xFFFFFF) != 0 && Math.Max((c >> 16) & 0xFF, Math.Max((c >> 8) & 0xFF, c & 0xFF)) > 2;
            foreach (double f in new[] { 0.25, 0.5, 0.75 })
            {
                int[] row = GrabLum(gx, gy + R(ch * f), cw, 1);
                for (int i = 0; i < cw; i++)
                    if (Lit(row[i])) { vl = Math.Min(vl, i); break; }
                for (int i = cw - 1; i >= 0; i--)
                    if (Lit(row[i])) { vr = Math.Max(vr, i); break; }
                int[] col = GrabLum(gx + R(cw * f), gy, 1, ch);
                for (int i = 0; i < ch; i++)
                    if (Lit(col[i])) { vt = Math.Min(vt, i); break; }
                for (int i = ch - 1; i >= 0; i--)
                    if (Lit(col[i])) { vb = Math.Max(vb, i); break; }
            }
        }

        // 검사(--perftest): 실제 화면에서 판별 한 번에 드는 시간 (게임 영역 재기, 게임 영역 찍기, 줄여서 판별)
        public static string Perf()
        {
            if (!GameClient(out int gx, out int gy, out int cw, out int ch))
                return "게임 창 없음";
            double T(Action a)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < 20; i++)
                    a();
                return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / 20;
            }
            double m = T(() => Measure(gx, gy, cw, ch));
            GameView(out int x, out int y, out int w, out int h);
            Frame f = null;
            double g = T(() => f = GrabFull(x, y, w, h, f));
            double s = T(() => GetStats(Shrink(f), null));
            double v = T(() => GameView(out _, out _, out _, out _));
            double rs = T(() => App.ResultScreen());
            f.Free();
            return string.Format(IC, "게임 창 {0}x{1}, 게임 영역 {2}x{3} / 영역 재기 {4:F1}ms, 영역 찍기 {5:F1}ms, 줄여서 판별 {6:F1}ms, 판별 중 GameView {7:F2}ms, 결과 화면 판단 {8:F1}ms", cw, ch, w, h, m, g, s, v, rs);
        }

        // 파일 이름이 .jpg면 품질 95 JPG(2560x1440 기준 PNG의 4분의 1 안팎), 아니면 무손실 PNG로 저장
        static bool SaveImage(Frame f, string file)
        {
            try
            {
                using (Bitmap bmp = Image.FromHbitmap(f.Hbm))
                {
                    if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        ImageCodecInfo jpg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        using (var ps = new EncoderParameters(1))
                        {
                            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                            bmp.Save(file, jpg, ps);
                        }
                    }
                    else
                        bmp.Save(file, ImageFormat.Png);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
