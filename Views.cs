using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using static ZzzWipeout.Wipe;

namespace ZzzWipeout
{
    // 확인 창: 장면마다 미리보기(누르면 크게 열기)와 저장 버튼, 아래에 자동 저장·전체 저장·폴더 열기
    sealed class ViewWindow : UiWindow
    {
        sealed class Thumb { public int X, Y, K; public string Label; public Bitmap Img; }

        double s;
        int pad, tw, th, bh, bg, W, H, botY;
        readonly List<Thumb> thumbs = new List<Thumb>();
        readonly List<Point> empty = new List<Point>();
        RectangleF close, gear, folder, all, sw, auto;
        public string Status = "";

        public ViewWindow(bool overlay) : base("와이프아웃 캡처", overlay) { }

        // 장면마다 한 줄 (왼쪽부터 깨끗한 순서). 찍은 장수와 상관없이 창 크기가 같도록 늘 4줄 x 장면당 캡처 수(설정) 칸으로 놓고
        // 빈 칸은 어둡게 둔다. 화면 너비·높이 안에 들어가게 미리보기 크기를 정한다 (장면이 4개를 넘으면 앞의 4개만 보인다)
        public Size Build()
        {
            s = Native.ScreenDpi / 96;
            int rows = 4, cols = Cfg.Per;
            foreach (var row in Groups)  // 설정을 줄이기 전에 찍은 것도 다 보이게
                cols = Math.Max(cols, row.Count);
            pad = R(28 * s); int gap = R(18 * s), titleH = R(84 * s), cd = R(38 * s);
            bh = R(40 * s); bg = R(10 * s);
            double thMax = (Native.ScreenH * 0.85 - titleH - bh - pad) / rows - bg - bh - gap;  // 가운데에 놓으면 아래 끝 0.925h ("완료" 글자 위)
            tw = R(Math.Min(440 * s, Math.Min((Native.ScreenW * 0.9 - 2 * pad - (cols - 1) * gap) / cols, thMax / Ratio)));
            th = R(tw * Ratio);
            W = 2 * pad + cols * tw + (cols - 1) * gap;
            botY = titleH + rows * (th + bg + bh + gap);
            int gridW = W - 2 * pad;
            W = Math.Max(W, 2 * pad + R(640 * s));  // 장면당 1~2장이어도 아래 줄(자동 저장·전체 저장·폴더 열기)과 제목이 들어가게
            int gx = (W - gridW) / 2;  // 창이 미리보기 칸보다 넓으면 칸을 가운데에 (좌우 여백을 같게)
            H = botY + bh + pad;
            close = new RectangleF(W - pad - cd, R(24 * s), cd, cd);
            gear = new RectangleF(close.X - R(12 * s) - cd, close.Y, cd, cd);  // 닫기 왼쪽: 설정 열기
            int bw = R(190 * s);
            Rects.Add(("close", close));
            Rects.Add(("settings", gear));
            for (int r = 1; r <= rows; r++)
            {
                List<int> row = r <= Groups.Count ? Groups[r - 1] : new List<int>();
                for (int c = 1; c <= cols; c++)
                {
                    int x = gx + (c - 1) * (tw + gap), y = titleH + (r - 1) * (th + bg + bh + gap);
                    if (c > row.Count)
                    {
                        empty.Add(new Point(x, y));
                        continue;
                    }
                    int k = row[c - 1];  // Files 번호
                    thumbs.Add(new Thumb { X = x, Y = y, K = k, Label = r + "-" + c });
                    Rects.Add(("open" + k, new RectangleF(x, y, tw, th)));
                    Rects.Add(("save" + k, new RectangleF(x, y + th + bg, tw, bh)));
                }
            }
            foreach (Thumb t in thumbs)  // 미리보기는 처음 한 번만 줄여 둔다 (마우스를 올릴 때마다 다시 그려서)
                t.Img = LoadScaled(Files[t.K - 1], tw, th);
            // 아래 줄은 오른쪽 끝부터: 저장 폴더 열기, 전체 저장, 자동 저장 스위치(왼쪽에 이름)
            folder = new RectangleF(W - pad - bw, botY, bw, bh);
            all = new RectangleF(folder.X - gap - bw, botY, bw, bh);
            int swW = R(62 * s), swH = R(32 * s);
            sw = new RectangleF(all.X - R(28 * s) - swW, botY + (bh - swH) / 2, swW, swH);
            auto = new RectangleF(sw.X - R(130 * s), botY, R(130 * s) + swW, bh);  // 이름과 스위치를 함께 누를 수 있게
            Rects.Add(("auto", auto));
            Rects.Add(("all", all));
            Rects.Add(("folder", folder));
            Status = Note != "" ? Note : "미리보기를 누르면 크게 열립니다";
            return new Size(W, H);
        }

        public static Bitmap LoadScaled(string file, int w, int h)
        {
            var tb = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            try
            {
                using (var img = Image.FromFile(file))
                using (var g = Graphics.FromImage(tb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;  // 고품질 쌍삼차
                    g.DrawImage(img, 0, 0, w, h);
                }
            }
            catch { }
            return tb;
        }

        public override void Draw(int? x = null, int? y = null)
        {
            if (!IsHandleCreated || IsDisposed)
                return;
            var c = new Canvas(W, H);
            Graphics g = c.G;
            D.Frame(g, W, H, s, pad, "WIPEOUT", Groups.Count + " SCENES  ·  " + Files.Count + " SHOTS", close, Hot, gear);
            // 장면: 둥근 미리보기 + 테(마우스를 올리면 노랑) + 왼쪽 위 번호 띠, 아래에 저장 버튼
            foreach (Point e in empty)  // 빈 칸: 미리보기·버튼 자리를 어두운 판으로
                D.Panel(g, e.X, e.Y, tw, th + bg + bh, s, false);
            foreach (Thumb t in thumbs)
            {
                bool on = Hot == "open" + t.K;
                using (GraphicsPath rp = D.RoundRect(t.X, t.Y, tw, th, 10 * s))
                {
                    g.SetClip(rp);
                    g.DrawImage(t.Img, t.X, t.Y, tw, th);
                    g.ResetClip();
                    using (var pen = new Pen(D.C(on ? 0xFFFFC83C : 0xFF3A3A3A), (float)((on ? 3 : 2) * s)))
                        g.DrawPath(pen, rp);
                }
                D.Slant(g, t.X + 12 * s, t.Y + 12 * s, 46 * s, 26 * s, 6 * s, 0xE0000000);
                D.Text(g, t.Label, t.X + 12 * s, t.Y + 12 * s, 52 * s, 26 * s, 16 * s, on ? 0xFFFFC83C : 0xFFFFFFFF);
                D.BtnPaint(g, tw, bh, bh / 0.0533, "저장", x: t.X, y: t.Y + th + bg, hot: Hot == "save" + t.K, check: Wipe.Saved.Contains(t.K));
            }
            // 아래 오른쪽: 자동 저장 스위치, 전체 저장, 저장 폴더 열기
            D.Text(g, "자동 저장", auto.X, botY, sw.X - auto.X - 12 * s, bh, 17 * s, Hot == "auto" ? 0xFFFFC83C : 0xFFFFFFFF, 2);
            D.Toggle(g, sw.X, sw.Y, sw.Width, sw.Height, Cfg.Auto, Hot == "auto");
            D.BtnPaint(g, all.Width, bh, bh / 0.0533, "전체 저장", x: all.X, y: botY, hot: Hot == "all");
            D.BtnPaint(g, folder.Width, bh, bh / 0.0533, "저장 폴더 열기", x: folder.X, y: botY, hot: Hot == "folder");
            c.End(Handle, x, y);
        }

        public override void Act(string act)
        {
            if (act == "close")
                App.ViewClose();
            else if (act == "settings")
                App.ShowSettings();
            else if (act == "auto")  // 설정 창의 자동 저장과 같은 값
            {
                Cfg.Auto = !Cfg.Auto;
                Cfg.Save();
                Draw();
                App.SV?.Draw();
            }
            else if (act == "all")
                App.Save(0);
            else if (act == "folder")
                App.OpenDir();
            else if (act.StartsWith("open"))
                App.ShowPhoto(int.Parse(act.Substring(4)));
            else
                App.Save(int.Parse(act.Substring(4)));
        }

        public override void CloseUi() => App.ViewClose();

        protected override void Dispose(bool disposing)
        {
            foreach (Thumb t in thumbs)
                t.Img?.Dispose();
            thumbs.Clear();
            base.Dispose(disposing);
        }
    }

    // 사진 크게 보기: 확인 창 위에 게임 화면의 85% 높이 안으로 띄운다. 좌우 원형 버튼(창이 포커스를 받으면 ←·→ 키도)으로
    // 이전/다음 사진(끝에서는 처음으로), 아래 저장 버튼. X·우클릭·ESC로 닫으면 확인 창으로 돌아간다
    sealed class PhotoWindow : UiWindow
    {
        double s;
        int pad, W, H, ix, iy, iw, ih, bh;
        RectangleF close, prev, next, save;
        public int K;
        string msg = "";
        Bitmap img;

        public PhotoWindow(bool overlay) : base("와이프아웃 사진", overlay) { }

        public Size Build(int k, int gw, int gh)
        {
            s = Native.ScreenDpi / 96;
            pad = R(28 * s); int titleH = R(84 * s), cd = R(38 * s);
            bh = R(44 * s);
            int ad = R(64 * s), side = pad + ad + R(18 * s), gp = R(16 * s);
            double fw = gw * 0.94 - 2 * side, fh = fw * Ratio, maxIh = gh * 0.85 - titleH - gp - bh - pad;  // 아래 끝을 "완료" 글자 위로
            if (fh > maxIh)
            {
                fh = maxIh;
                fw = fh / Ratio;
            }
            iw = R(fw); ih = R(fh);
            W = iw + 2 * side; H = titleH + ih + gp + bh + pad;
            ix = side; iy = titleH;
            close = new RectangleF(W - pad - cd, R(24 * s), cd, cd);
            prev = new RectangleF(pad, titleH + (ih - ad) / 2, ad, ad);
            next = new RectangleF(W - pad - ad, titleH + (ih - ad) / 2, ad, ad);
            save = new RectangleF(W - side - R(190 * s), titleH + ih + gp, R(190 * s), bh);
            K = k;
            Rects.Add(("close", close));
            Rects.Add(("prev", prev));
            Rects.Add(("next", next));
            Rects.Add(("save", save));
            LoadImg();
            return new Size(W, H);
        }

        // 지금 사진을 보이는 크기로 한 번만 줄여 둔다 (마우스를 올릴 때마다 다시 그려서)
        void LoadImg()
        {
            img?.Dispose();
            img = ViewWindow.LoadScaled(Files[K - 1], iw, ih);
        }

        public override void Draw(int? x = null, int? y = null)
        {
            if (!IsHandleCreated || IsDisposed)
                return;
            string label = K.ToString();  // 장면-순번 이름 (확인 창의 번호 띠와 같게)
            for (int r = 0; r < Groups.Count; r++)
                for (int c2 = 0; c2 < Groups[r].Count; c2++)
                    if (Groups[r][c2] == K)
                        label = (r + 1) + "-" + (c2 + 1);
            var c = new Canvas(W, H);
            Graphics g = c.G;
            D.Frame(g, W, H, s, pad, "장면 " + label, K + " / " + Files.Count, close, Hot);
            using (GraphicsPath rp = D.RoundRect(ix, iy, iw, ih, 12 * s))
            {
                g.SetClip(rp);
                g.DrawImage(img, ix, iy, iw, ih);
                g.ResetClip();
                using (var pen = new Pen(D.C(0xFF3A3A3A), (float)(2 * s)))
                    g.DrawPath(pen, rp);
            }
            // 이전·다음: 닫기와 같은 검은 원 + 회색 테(마우스를 올리면 노랑) + 흰 꺾쇠
            using (var br = new SolidBrush(D.C(0xFF050505)))
            using (var pen = new Pen(D.C(0xFF3A3A3A), (float)(2 * s)) { LineJoin = LineJoin.Round })
                foreach (int d in new[] { -1, 1 })
                {
                    RectangleF b = d < 0 ? prev : next;
                    double cx = b.X + b.Width / 2.0, cy = b.Y + b.Height / 2.0, k = 10 * s;
                    g.FillEllipse(br, b);
                    pen.Width = (float)(2 * s);
                    pen.Color = D.C(Hot == (d < 0 ? "prev" : "next") ? 0xFFFFC83C : 0xFF4A4A4A);
                    g.DrawEllipse(pen, b);
                    pen.Width = (float)(3.5 * s);
                    pen.Color = D.C(0xFFFFFFFF);
                    g.DrawLines(pen, new[] { new PointF((float)(cx - d * k / 2), (float)(cy - k)), new PointF((float)(cx + d * k / 2), (float)cy), new PointF((float)(cx - d * k / 2), (float)(cy + k)) });
                }
            if (msg != "")
                D.Text(g, msg, ix, save.Y, save.X - ix - 24 * s, bh, 15 * s, 0xFF9A9A9A, 2, 0.2, "Noto Sans KR Medium", 0, 5);
            D.BtnPaint(g, save.Width, bh, bh / 0.0533, "이 사진 저장", x: save.X, y: save.Y, hot: Hot == "save", check: Wipe.Saved.Contains(K));
            c.End(Handle, x, y);
        }

        public override void Act(string act)
        {
            if (act == "close")
            {
                App.PhotoClose();
                return;
            }
            if (act == "save")
                msg = App.Save(K);
            else
                Go(act == "next" ? 1 : -1);
            Draw();
        }

        public override void Key(int vk)
        {
            if (vk == 37 || vk == 39)  // ← →
            {
                Go(vk == 39 ? 1 : -1);
                Draw();
            }
        }

        void Go(int d)
        {
            int n = Files.Count;
            K = (K - 1 + d + n) % n + 1;
            msg = "";
            LoadImg();
        }

        public override void CloseUi() => App.PhotoClose();

        protected override void Dispose(bool disposing)
        {
            img?.Dispose();
            img = null;
            base.Dispose(disposing);
        }
    }

    // 설정 창
    sealed class SettingsWindow : UiWindow
    {
        sealed class Row { public double X, Y, W, H, DescW; public string Label, Act, Desc; }

        double s;
        int pad, W, H, bh, botY;
        readonly List<Row> rows = new List<Row>();
        RectangleF close, chg, opn, t1, t2, t3, t4, t5, minus, num, plus, done;

        public SettingsWindow() : base("젠존제 와이프아웃 설정", false) { }

        public Size Build()
        {
            s = Native.ScreenDpi / 96;
            pad = R(28 * s); W = R(700 * s); int titleH = R(84 * s), cd = R(38 * s);
            int rh = R(80 * s), rg = R(12 * s), bw = R(96 * s), sbh = R(40 * s);
            bh = R(44 * s);
            int tw = R(62 * s), th = R(32 * s), rx = pad, rw = W - 2 * pad, y = titleH + R(4 * s);
            close = new RectangleF(W - pad - cd, R(24 * s), cd, cd);
            RectangleF Tog(int yy) => new RectangleF(rx + rw - R(22 * s) - tw, yy + (rh - th) / 2, tw, th);
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "저장 폴더", Act = "", DescW = rw - R(20 * s) - 2 * bw - R(46 * s) });
            chg = new RectangleF(rx + rw - R(18 * s) - 2 * bw - R(10 * s), y + (rh - sbh) / 2, bw, sbh);
            opn = new RectangleF(rx + rw - R(18 * s) - bw, y + (rh - sbh) / 2, bw, sbh);
            y += rh + rg;
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "자동 저장", Act = "auto", DescW = rw - R(120 * s), Desc = "캡처가 끝나면 모든 장면을 저장 폴더에 바로 저장합니다" });
            t1 = Tog(y);
            y += rh + rg;
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "결과 화면 버튼", Act = "btn", DescW = rw - R(120 * s), Desc = "도전 결과 화면의 '한 번 더' 옆에 와이프아웃 버튼을 띄웁니다" });
            t2 = Tog(y);
            y += rh + rg;
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "원본 화질 (PNG)", Act = "png", DescW = rw - R(120 * s), Desc = "켜면 무손실 PNG(장당 4~5MB), 끄면 고화질 JPG(장당 1MB 안팎)로 저장합니다" });
            t3 = Tog(y);
            y += rh + rg;
            // 장면당 캡처 수: 오른쪽에 [-] 숫자 [+]
            int pw = R(52 * s), nw = R(56 * s);
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "장면당 캡처 수", Act = "", DescW = rw - R(40 * s) - 2 * pw - nw, Desc = "한 장면에서 깨끗한 순서로 남길 장수 (1~10장)" });
            minus = new RectangleF(rx + rw - R(22 * s) - 2 * pw - nw, y + (rh - sbh) / 2, pw, sbh);
            num = new RectangleF(minus.X + pw, y, nw, rh);
            plus = new RectangleF(num.X + nw, minus.Y, pw, sbh);
            y += rh + rg;
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "녹화에 보이기", Act = "rec", DescW = rw - R(120 * s), Desc = "켜면 결과 화면 버튼과 확인 창이 녹화·스크린샷에 찍힙니다" });
            t4 = Tog(y);
            y += rh + rg;
            rows.Add(new Row { X = rx, Y = y, W = rw, H = rh, Label = "Windows 로그인 시 자동 실행", Act = "boot", DescW = rw - R(120 * s), Desc = "켜면 Windows에 로그인할 때 이 프로그램이 트레이에서 켜집니다" });
            t5 = Tog(y);
            y += rh + rg;
            botY = y + R(6 * s);
            H = botY + bh + pad;
            done = new RectangleF(W - pad - R(160 * s), botY, R(160 * s), bh);
            RectangleF RowR(int i) => new RectangleF((float)rows[i].X, (float)rows[i].Y, (float)rows[i].W, (float)rows[i].H);
            Rects.Add(("close", close)); Rects.Add(("dir", chg)); Rects.Add(("opendir", opn));
            Rects.Add(("auto", RowR(1))); Rects.Add(("btn", RowR(2))); Rects.Add(("png", RowR(3)));
            Rects.Add(("per-", minus)); Rects.Add(("per+", plus)); Rects.Add(("rec", RowR(5))); Rects.Add(("boot", RowR(6))); Rects.Add(("done", done));
            return new Size(W, H);
        }

        public override void Draw(int? x = null, int? y = null)
        {
            if (!IsHandleCreated || IsDisposed)
                return;
            var c = new Canvas(W, H);
            Graphics g = c.G;
            D.Frame(g, W, H, s, pad, "설정", "WIPEOUT CAPTURE", close, Hot);
            rows[0].Desc = App.SaveDirs()[0] + (Cfg.Dir == "" ? "  (기본)" : "");
            foreach (Row r in rows)
            {
                D.Panel(g, r.X, r.Y, r.W, r.H, s, r.Act != "" && Hot == r.Act);
                D.Text(g, r.Label, r.X + 20 * s, r.Y + 10 * s, r.DescW, 32 * s, 19 * s, 0xFFFFFFFF, 0);
                D.Text(g, r.Desc, r.X + 22 * s, r.Y + 42 * s, r.DescW, 26 * s, 14 * s, 0xFF8A8A8A, 0, 0.2, "Noto Sans KR Medium", 0, 5);
            }
            D.BtnPaint(g, chg.Width, chg.Height, chg.Height / 0.0533, "변경", x: chg.X, y: chg.Y, hot: Hot == "dir");
            D.BtnPaint(g, opn.Width, opn.Height, opn.Height / 0.0533, "열기", x: opn.X, y: opn.Y, hot: Hot == "opendir");
            D.Toggle(g, t1.X, t1.Y, t1.Width, t1.Height, Cfg.Auto, Hot == "auto");
            D.Toggle(g, t2.X, t2.Y, t2.Width, t2.Height, Cfg.Btn, Hot == "btn");
            D.Toggle(g, t3.X, t3.Y, t3.Width, t3.Height, Cfg.Png, Hot == "png");
            D.Toggle(g, t4.X, t4.Y, t4.Width, t4.Height, Cfg.Rec, Hot == "rec");
            D.Toggle(g, t5.X, t5.Y, t5.Width, t5.Height, Cfg.BootOn(), Hot == "boot");
            D.BtnPaint(g, minus.Width, minus.Height, minus.Height / 0.0533, "−", x: minus.X, y: minus.Y, hot: Hot == "per-");
            D.Text(g, Cfg.Per.ToString(), num.X, num.Y, num.Width, num.Height, 22 * s, 0xFFFFC83C);
            D.BtnPaint(g, plus.Width, plus.Height, plus.Height / 0.0533, "+", x: plus.X, y: plus.Y, hot: Hot == "per+");
            D.Text(g, "바꾼 설정은 바로 저장됩니다", pad + 4 * s, botY, done.X - pad - 20 * s, bh, 14 * s, 0xFF6A6A6A, 0, 0.2, "Noto Sans KR Medium", 0);
            D.BtnPaint(g, done.Width, bh, bh / 0.0533, "닫기", x: done.X, y: botY, hot: Hot == "done");
            c.End(Handle, x, y);
        }

        public override void Act(string act)
        {
            if (act == "close" || act == "done")
            {
                App.SettingsClose();
                return;
            }
            if (act == "auto")
                Cfg.Auto = !Cfg.Auto;
            else if (act == "btn")
                Cfg.Btn = !Cfg.Btn;
            else if (act == "png")
                Cfg.Png = !Cfg.Png;
            else if (act == "per-" || act == "per+")
                Cfg.Per = Math.Max(1, Math.Min(10, Cfg.Per + (act == "per+" ? 1 : -1)));
            else if (act == "rec")  // 지금 떠 있는 버튼·창에도 바로 적용
            {
                Cfg.Rec = !Cfg.Rec;
                App.CaptureAll();
            }
            else if (act == "boot")
                Cfg.BootSet(!Cfg.BootOn());
            else if (act == "opendir")
            {
                App.OpenDir();
                return;
            }
            else if (act == "dir")
            {
                using (var dlg = new FolderBrowserDialog { Description = "와이프아웃 장면을 저장할 폴더를 고르세요", SelectedPath = App.SaveDirs()[0], ShowNewFolderButton = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedPath == "")
                        return;
                    Cfg.Dir = dlg.SelectedPath;
                }
            }
            Cfg.Save();
            Draw();
            App.WV?.Draw();  // 확인 창의 자동 저장 스위치도 같게
        }

        public override void CloseUi() => App.SettingsClose();
    }
}
