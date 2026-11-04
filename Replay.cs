using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZzzWipeout
{
    // 재생 검사 (ZZZ-Wipeout.exe --replay 폴더): 폴더\frames\*.png를 순서대로 화면 대신 넣고 시계를 45ms씩 돌려
    // 실제와 같은 판별·선택을 한다. 결과는 폴더\cs에 남긴다 (log.txt, selected.txt, files\)
    static class Replay
    {
        public static void Run(string dir)
        {
            string[] files = Directory.GetFiles(Path.Combine(dir, "frames"), "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            string outDir = Path.Combine(dir, "cs");
            try { Directory.Delete(outDir, true); } catch { }
            Directory.CreateDirectory(outDir);
            Wipe.Dir = Path.Combine(outDir, "files");
            Wipe.LogFile = Path.Combine(outDir, "log.txt");
            Cfg.Per = 3; Cfg.Auto = false; Cfg.Debug = false; Cfg.Png = false;

            var sel = new StringBuilder();
            Wipe.ReplaySaved = (f, name) => sel.Append(Path.GetFileName(name) + " " + f.Src + "\n");
            Wipe.Replaying = true;
            Frame screen = null;
            void Show(int i)  // i번째(0부터) 프레임을 화면으로. 영상이 끝나면 검은 화면
            {
                screen = i < files.Length ? Load(files[i], screen, i + 1) : Black(screen.W, screen.H, screen, i + 1);
                Wipe.FakeScreen = screen;
            }

            // 시작: 위쪽 줄이 노래지는 첫 프레임 (실제 프로그램의 0.1초 감시와 같은 규칙. 그 전 프레임으로 게임 영역도 익힌다)
            int start = -1;
            for (int i = 0; i < files.Length && start < 0; i++)
            {
                Show(i);
                if (Wipe.Top(true))
                    start = i;
            }
            if (start < 0)
            {
                File.WriteAllText(Path.Combine(outDir, "selected.txt"), "시작 못 찾음\n");
                return;
            }
            Wipe.ReplayNow = 100000;
            Wipe.Start();
            for (int idx = start + 1; Wipe.On && idx < files.Length + 40; idx++)
            {
                Show(idx);
                Wipe.ReplayNow += 45;
                Wipe.Tick();
            }
            File.WriteAllText(Path.Combine(outDir, "selected.txt"), "시작 " + (start + 1) + (Wipe.On ? " (프레임이 모자라 안 끝남)" : "") + "\n" + sel);
        }

        // 결과 화면 검사 (ZZZ-Wipeout.exe --resulttest 결과파일 그림...): 그림을 차례로 화면 대신 넣고, 그림마다 결과 화면 판단과
        // 버튼 자리를 결과파일에 한 줄씩 남긴다. 게임 영역은 앞서 넣은 그림까지 모두 보고 정한다 (실제로 전투 화면을 거치는 것처럼)
        public static void ResultTest(string[] args)
        {
            var o = new StringBuilder();
            Frame screen = null;
            for (int i = 2; i < args.Length; i++)
            {
                screen = Load(args[i], screen, i);
                Wipe.FakeScreen = screen;
                for (int k = 0; k < 5; k++)
                    Wipe.GameView(out _, out _, out _, out _);
                o.Append(Path.GetFileName(args[i]) + ": " + App.ResultInfo() + "\n");
            }
            File.WriteAllText(args[1], o.ToString());
        }

        static Frame Black(int w, int h, Frame f, int src)
        {
            if (f == null || f.W != w || f.H != h)
            {
                f?.Free();
                f = new Frame(w, h);
            }
            Marshal.Copy(new byte[w * h * 4], 0, f.Bits, w * h * 4);
            f.Src = src;
            return f;
        }

        // PNG를 32비트 top-down DIB에 그대로 옮긴다 (GDI+ LockBits에 DIB 메모리를 직접 넘김)
        static Frame Load(string file, Frame f, int src)
        {
            using (var bmp = new Bitmap(file))
            {
                if (f == null || f.W != bmp.Width || f.H != bmp.Height)
                {
                    f?.Free();
                    f = new Frame(bmp.Width, bmp.Height);
                }
                var bd = new BitmapData { Width = f.W, Height = f.H, Stride = f.W * 4, PixelFormat = PixelFormat.Format32bppRgb, Scan0 = f.Bits };
                bmp.LockBits(new Rectangle(0, 0, f.W, f.H), ImageLockMode.ReadOnly | ImageLockMode.UserInputBuffer, PixelFormat.Format32bppRgb, bd);
                bmp.UnlockBits(bd);
            }
            f.Src = src;
            return f;
        }
    }
}
