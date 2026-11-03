using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static ZzzWipeout.Native;

namespace ZzzWipeout
{
    // 그리기 판: W x H 투명 그림(DIB)과 그 위에 그릴 GDI+ 판. 다 그리면 End로 레이어드 창에 올린다
    sealed class Canvas
    {
        public readonly int W, H;
        public readonly Graphics G;
        readonly IntPtr sdc, mdc, dib, old;
        readonly Bitmap gb;

        public Canvas(int w, int h)
        {
            W = w; H = h;
            sdc = GetDC(IntPtr.Zero);
            mdc = CreateCompatibleDC(sdc);
            dib = NewDib(mdc, w, h, out IntPtr bits);
            old = SelectObject(mdc, dib);
            gb = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            G = Graphics.FromImage(gb);
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.TextRenderingHint = TextRenderingHint.AntiAlias;
        }

        // 그린 그림을 창에 올린다. x, y를 주면 그 자리로 옮기고, 안 주면 지금 자리 그대로
        public void End(IntPtr hwnd, int? x = null, int? y = null)
        {
            G.Dispose();
            gb.Dispose();
            var sz = new SIZE { W = W, H = H };
            var src = new POINT();
            var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 };  // AC_SRC_OVER, AC_SRC_ALPHA
            if (x.HasValue)
            {
                var pt = new POINT { X = x.Value, Y = y.Value };
                UpdateLayeredWindow(hwnd, sdc, ref pt, ref sz, mdc, ref src, 0, ref blend, 2);  // ULW_ALPHA
            }
            else
                UpdateLayeredWindowNoPos(hwnd, sdc, IntPtr.Zero, ref sz, mdc, ref src, 0, ref blend, 2);
            SelectObject(mdc, old);
            DeleteObject(dib);
            DeleteDC(mdc);
            ReleaseDC(IntPtr.Zero, sdc);
        }
    }

    static class D
    {
        public static Color C(uint argb) => Color.FromArgb(unchecked((int)argb));
        static float F(double v) => (float)v;

        static FontFamily Family(string name)
        {
            try { return new FontFamily(name); }
            catch { return new FontFamily("Malgun Gothic"); }
        }

        static Font MakeFont(FontFamily fam, double px, int style)
        {
            try { return new Font(fam, F(px), (FontStyle)style, GraphicsUnit.Pixel); }
            catch { return new Font(fam, F(px), FontStyle.Regular, GraphicsUnit.Pixel); }
        }

        // 게임 결과 화면 버튼 모양 (2000x1125 캡처에서 잰 값): 알약형, 테 두께 0.0053h(위 밝기 73 → 아래 48),
        // 검은 바탕에 가는 대각선 격자 무늬(밝기 0~18), 흰 글자는 굵고 오른쪽으로 기울어짐(높이 0.022h).
        // 게임 글꼴은 게임 파일 안에 있어 쓸 수 없어서 설치된 굵은 한글 글꼴로 비슷하게 그린다
        // (x, y)에 그린다. hot이면(마우스가 올라가 있으면) 테를 노랗게
        // check면 글자 오른쪽에 초록 체크 표시 (이미 저장한 사진)
        public static void BtnPaint(Graphics g, double bw, double bh, double h, string text, string fontName = "Noto Sans KR Black", double x = 0, double y = 0, bool hot = false, bool check = false)
        {
            double t = h * 0.0045, d = bh - t;  // 테 두께, 테 가운데 선 기준 반원 지름
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;  // 투명 바탕이라 ClearType 대신 안티에일리어싱
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddArc(F(x + t / 2), F(y + t / 2), F(d), F(d), 90, 180);
                path.AddArc(F(x + bw - t / 2 - d), F(y + t / 2), F(d), F(d), 270, 180);
                path.CloseFigure();
                using (var br = new SolidBrush(C(0xFF020202)))
                    g.FillPath(br, path);
                using (var hatch = new HatchBrush(HatchStyle.DiagonalCross, C(0xFF0D0D0D), C(0xFF020202)))
                    g.FillPath(hatch, path);
                using (var lb = new LinearGradientBrush(new PointF(0, F(y)), new PointF(0, F(y + bh)), C(hot ? 0xFFFFD040 : 0xFF444444), C(hot ? 0xFFD49A1A : 0xFF2C2C2C)))
                using (var pen = new Pen(lb, F(t)))
                {
                    lb.WrapMode = WrapMode.Tile;
                    g.DrawPath(pen, path);
                }
            }
            Text(g, text, x, y, bw, bh, h * 0.024, 0xFFFFFFFF, 1, 0.2, fontName);
            if (!check)
                return;
            double tw;
            using (var fam = Family(fontName))
            using (var font = MakeFont(fam, h * 0.024, 1))
                tw = g.MeasureString(text, font).Width;
            double k = h * 0.011, cx = x + bw / 2 + tw / 2 + k * 0.6, cy = y + bh / 2;  // 체크 왼쪽 끝, 가운데 높이
            using (var pen = new Pen(C(0xFF3CDC64), F(h * 0.0045)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(F(cx), F(cy)), new PointF(F(cx + k * 0.4), F(cy + k * 0.45)), new PointF(F(cx + k * 1.15), F(cy - k * 0.5)) });
        }

        // 글자를 (x, y, w, h) 칸에 그린다. align 0 왼쪽·1 가운데·2 오른쪽, px 글자 크기(픽셀), style 1 굵게,
        // trim 5면 칸을 넘는 경로를 가운데를 "…"로 줄인다.
        // 게임 글자처럼 기울이려고 기울임 글꼴 대신 가로로 기울이는 변환(x' = x - sh·y)을 걸고, 칸 가운데가 그대로 있게 옮긴다
        public static void Text(Graphics g, string text, double x, double y, double w, double h, double px, uint color, int align = 1, double sh = 0.2,
            string fontName = "Noto Sans KR Black", int style = 1, int trim = 0)
        {
            using (var fam = Family(fontName))
            using (var font = MakeFont(fam, px, style))
            using (var fmt = new StringFormat(StringFormatFlags.NoWrap))  // 줄바꿈 안 함
            using (var br = new SolidBrush(C(color)))
            using (var m = new Matrix(1, 0, F(-sh), 1, F(sh * (y + h / 2)), 0))
            {
                fmt.Alignment = (StringAlignment)align;
                fmt.LineAlignment = StringAlignment.Center;
                if (trim != 0)
                    fmt.Trimming = (StringTrimming)trim;
                g.Transform = m;
                g.DrawString(text, font, br, new RectangleF(F(x), F(y), F(w), F(h)), fmt);
                g.ResetTransform();
            }
        }

        public static GraphicsPath RoundRect(double x, double y, double w, double h, double r)
        {
            var p = new GraphicsPath(FillMode.Alternate);
            p.AddArc(F(x), F(y), F(2 * r), F(2 * r), 180, 90);
            p.AddArc(F(x + w - 2 * r), F(y), F(2 * r), F(2 * r), 270, 90);
            p.AddArc(F(x + w - 2 * r), F(y + h - 2 * r), F(2 * r), F(2 * r), 0, 90);
            p.AddArc(F(x), F(y + h - 2 * r), F(2 * r), F(2 * r), 90, 90);
            p.CloseFigure();
            return p;
        }

        // 기울어진 띠(평행사변형) 칠하기: (x, y)부터 너비 w·높이 h, 위쪽이 k만큼 오른쪽으로 밀림
        public static void Slant(Graphics g, double x, double y, double w, double h, double k, uint color)
        {
            var pts = new[] { new PointF(F(x + k), F(y)), new PointF(F(x + k + w), F(y)), new PointF(F(x + w), F(y + h)), new PointF(F(x), F(y + h)) };
            using (var br = new SolidBrush(C(color)))
                g.FillPolygon(br, pts, FillMode.Alternate);
        }

        // 창 틀: 둥근 검은 판 + 대각선 격자 + 회색 테, 굵은 제목, 오른쪽 회색 영문 부제,
        // 제목 아래 가는 선, 원형 닫기(검은 원 + 회색 테 + 흰 X. 마우스를 올리면 테가 노랑)
        // gear를 주면 닫기 왼쪽에 설정(톱니바퀴) 버튼도 그린다
        public static void Frame(Graphics g, double W, double H, double s, double pad, string title, string sub, RectangleF c, string hot, RectangleF? gear = null)
        {
            using (var bg = RoundRect(s, s, W - 2 * s, H - 2 * s, 16 * s))
            {
                using (var br = new SolidBrush(C(0xF6080808)))
                    g.FillPath(br, bg);
                using (var hatch = new HatchBrush(HatchStyle.DiagonalCross, C(0xFF141414), C(0x00000000)))
                    g.FillPath(hatch, bg);
                using (var pen = new Pen(C(0xFF3A3A3A), F(2 * s)))
                    g.DrawPath(pen, bg);
            }
            double ty = 24 * s, tH = 38 * s;
            Title(g, title, pad - 2 * s, ty - 8 * s, c.X - 200 * s - pad, tH + 14 * s, 42 * s, s);
            Text(g, sub, pad, ty, (gear?.X ?? c.X) - 18 * s - pad, tH, 15 * s, 0xFF8A8A8A, 2, 0.2, "Noto Sans KR Medium", 0);
            using (var pen = new Pen(C(0xFF262626), F(s)))
                g.DrawLine(pen, F(pad), F(70 * s), F(W - pad), F(70 * s));
            if (gear is RectangleF gr)  // 검은 원 + 테(마우스를 올리면 노랑) + 흰 톱니바퀴 (고리 + 톱니 8개)
            {
                double gx = gr.X + gr.Width / 2.0, gy = gr.Y + gr.Height / 2.0, r = 5.5 * s;
                using (var br = new SolidBrush(C(0xFF050505)))
                    g.FillEllipse(br, gr);
                using (var pen = new Pen(C(hot == "settings" ? 0xFFFFC83C : 0xFF4A4A4A), F(2 * s)))
                    g.DrawEllipse(pen, gr);
                using (var pen = new Pen(C(0xFFFFFFFF), F(3 * s)))
                {
                    g.DrawEllipse(pen, F(gx - r), F(gy - r), F(2 * r), F(2 * r));
                    pen.Width = F(3.2 * s);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4, ca = Math.Cos(a), sa = Math.Sin(a);
                        g.DrawLine(pen, F(gx + ca * (r + 1 * s)), F(gy + sa * (r + 1 * s)), F(gx + ca * (r + 4.5 * s)), F(gy + sa * (r + 4.5 * s)));
                    }
                }
            }
            double cx = c.X + c.Width / 2.0, cy = c.Y + c.Height / 2.0;
            using (var br = new SolidBrush(C(0xFF050505)))
                g.FillEllipse(br, c);
            using (var pen = new Pen(C(hot == "close" ? 0xFFFFC83C : 0xFF4A4A4A), F(2 * s)))
                g.DrawEllipse(pen, c);
            double k = 7 * s;
            using (var pen = new Pen(C(0xFFFFFFFF), F(2.5 * s)))
            {
                g.DrawLine(pen, F(cx - k), F(cy - k), F(cx + k), F(cy + k));
                g.DrawLine(pen, F(cx + k), F(cy - k), F(cx - k), F(cy + k));
            }
        }

        // 창 제목: 게임 메뉴 제목처럼 기울이지 않은 아주 굵은 글자에 위는 흰색·아래는 밝은 회색으로 내려가는 그라데이션,
        // 오른쪽 아래로 진한 그림자와 얇은 어두운 테두리. 글자 모양을 경로로 만들어 그림자·채우기·테두리를 차례로 그린다
        public static void Title(Graphics g, string text, double x, double y, double w, double h, double px, double s)
        {
            using (var fam = Family("Noto Sans KR Black"))
            using (var fmt = new StringFormat(StringFormatFlags.NoWrap))
            using (var path = new GraphicsPath(FillMode.Winding))  // Winding: 굵은 한글 획이 겹친 곳이 비지 않게
            {
                fmt.LineAlignment = StringAlignment.Center;
                path.AddString(text, fam, 1, F(px), new RectangleF(F(x), F(y), F(w), F(h)), fmt);
                // 테두리: 글자 모양을 넓힌 경로를 어둡게 깐다 (선으로 그으면 굵은 한글 획이 겹친 안쪽 윤곽까지 그어짐).
                // 그림자: 넓힌 경로를 오른쪽 아래로 옮겨 반투명 검정으로 먼저 깐다
                using (var op = (GraphicsPath)path.Clone())
                using (var pen = new Pen(C(0xFF000000), F(2.4 * s)) { LineJoin = LineJoin.Round })
                {
                    op.Widen(pen, null, 0.25f);
                    op.FillMode = FillMode.Winding;
                    using (var sp = (GraphicsPath)op.Clone())
                    using (var m = new Matrix(1, 0, 0, 1, F(2.5 * s), F(3.5 * s)))
                    using (var br = new SolidBrush(C(0xB0000000)))
                    {
                        sp.Transform(m);
                        g.FillPath(br, sp);
                        g.FillPath(br, path);  // 넓힌 경로의 안쪽 구멍까지 메운다
                        br.Color = C(0xFF141414);
                        g.FillPath(br, op);
                    }
                }
                // 채우기: 글자 위쪽 흰색 → 아래쪽 밝은 회색
                using (var lb = new LinearGradientBrush(new PointF(0, F(y + h * 0.2)), new PointF(0, F(y + h * 0.85)), C(0xFFFFFFFF), C(0xFFA8A8A8)))
                {
                    lb.WrapMode = WrapMode.TileFlipXY;
                    g.FillPath(lb, path);
                }
            }
        }

        // 설정 한 줄 바탕: 조금 밝은 검은 둥근 판 + 어두운 회색 테 (마우스를 올리면 노랑)
        public static void Panel(Graphics g, double x, double y, double w, double h, double s, bool hot)
        {
            using (var p = RoundRect(x, y, w, h, 10 * s))
            using (var br = new SolidBrush(C(0xFF0D0D0D)))
            using (var pen = new Pen(C(hot ? 0xFFFFC83C : 0xFF262626), F((hot ? 2 : 1.5) * s)))
            {
                g.FillPath(br, p);
                g.DrawPath(pen, p);
            }
        }

        // 켜기/끄기 스위치: 켜면 노란 바탕에 검은 손잡이가 오른쪽, 끄면 검은 바탕에 회색 손잡이가 왼쪽
        public static void Toggle(Graphics g, double x, double y, double w, double h, bool on, bool hot)
        {
            using (var p = RoundRect(x, y, w, h, h / 2))
            using (var br = new SolidBrush(C(on ? 0xFFFFC83C : 0xFF151515)))
            using (var pen = new Pen(C(on || hot ? 0xFFFFC83C : 0xFF4A4A4A), F(h * 0.07)))
            {
                g.FillPath(br, p);
                g.DrawPath(pen, p);
                double m = h * 0.18, d = h - 2 * m;
                br.Color = C(on ? 0xFF000000 : 0xFF8A8A8A);
                g.FillEllipse(br, F(on ? x + w - m - d : x + m), F(y + m), F(d), F(d));
            }
        }
    }
}
