using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static ZzzWipeout.Native;

namespace ZzzWipeout
{
    // 직접 그리는 창(확인 창·사진 보기·설정 창): 게임 화면처럼 보이게 창 틀 없이 전부 직접 그린다 (검은 격자 바탕,
    // 굵은 제목, 원형 닫기, 알약 버튼, 마우스를 올리면 노란 테). 빈 곳을 끌면 창이 움직이고, ESC·우클릭으로 닫는다.
    // overlay면 게임 창에 딸린 창(소유 창)으로 띄운다: 늘 게임 바로 위에 있고, 알트탭·최소화 때 게임과 한 덩어리로 움직이며
    // 알트탭 목록·작업표시줄에 따로 나오지 않는다. 뜰 때는 포커스를 뺏지 않는다 (마우스가 올라오면 App.Grab이 가져감)
    abstract class UiWindow : Form
    {
        public readonly bool Overlay;
        public string Hot = "", Down = "";
        public List<(string act, RectangleF r)> Rects = new List<(string, RectangleF)>();
        bool track;

        // 게임 위 창의 주인(소유자) 창. 클릭 검사(ClickTest)에서는 가짜 게임 창으로 바꾼다
        public static Func<IntPtr> OverlayOwner = GameWindow;
        IntPtr owner;

        protected UiWindow(string title, bool overlay)
        {
            Overlay = overlay;
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;  // false로 두면 WinForms가 숨김용 자체 창을 주인으로 끼워 넣어 게임 창에 딸리지 못한다. 게임 위 창은 아래 CreateParams에서 APPWINDOW를 빼서 작업표시줄·알트탭에 안 나온다
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000;  // LAYERED
                if (Overlay)
                {
                    cp.ExStyle |= 0x08000000 | 0x80;  // NOACTIVATE, TOOLWINDOW
                    cp.ExStyle &= ~0x40000;  // APPWINDOW 빼기: 있으면 딸린 창이어도 알트탭 목록에 따로 나와서 게임과 번갈아 전환됨
                    owner = OverlayOwner();
                    if (owner != IntPtr.Zero)
                        cp.Parent = owner;  // 만들 때부터 게임 창에 딸린 창으로 (만든 뒤 바꾸면 게임 창 아래에 깔림)
                    else  // 게임 창이 없으면 그냥 맨 위에
                        cp.ExStyle |= 0x8;  // TOPMOST
                }
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => Overlay;
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public abstract void Draw(int? x = null, int? y = null);
        public abstract void Act(string act);
        public abstract void CloseUi();
        public virtual void Key(int vk) { }

        // 창을 만들어 (x, y)에 그려 띄운다
        public void Open(int x, int y)
        {
            IntPtr h = Handle;
            if (Overlay)  // 녹화에 보이기를 끄면 화면 캡처에서 뺀다. 켜도 판별과 안 겹치게 창 아래 끝을 "완료" 글자(0.936h) 위로 둔다
                App.Capture(h);
            Draw(x, y);
            if (Overlay)
            {
                if (owner != IntPtr.Zero && GetWindow(h, 4) != owner)  // GW_OWNER: 만들 때 지정이 안 먹었으면 다시
                    SetWindowLongPtr(h, -8, owner);
                ShowWindow(h, 4);  // SW_SHOWNOACTIVATE
                if (owner != IntPtr.Zero)
                    SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x13);  // HWND_TOP, NOSIZE|NOMOVE|NOACTIVATE: 게임 바로 위로
                return;
            }
            ShowWindow(h, 5);
            SetForegroundWindow(h);
            Activate();
        }

        protected void SetHot(string act)
        {
            if (act == Hot)
                return;
            Hot = act;
            Draw();
        }

        // ESC로 닫기, 그 밖의 키는 창마다 (사진 보기: ←·→)
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Escape)
                BeginInvoke((Action)CloseUi);
            else
                Key((int)k);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x6:  // WM_ACTIVATE: 알트탭으로 게임에 돌아오면 Windows가 게임 대신 이 창을 활성화한다. 마우스가 창 밖이면 게임에 돌려준다
                    if (Overlay && ((long)m.WParam & 0xFFFF) != 0 && !Bounds.Contains(Cursor.Position))
                        BeginInvoke((Action)App.GiveBack);
                    break;
                case 0x104:  // WM_SYSKEYDOWN: Alt를 누르면 포커스를 바로 게임에 돌려준다. 우리 창은 클릭을 막으려고 포커스를 빌릴 뿐이라,
                             // 알트탭은 게임에서 시작해야 게임과 (딸린) 우리 창이 함께 내려간다 (우리 창에서 시작하면 직전 프로그램인 게임으로 돌아감)
                    if (Overlay && (int)m.WParam == 0x12)
                    {
                        App.GiveBack();
                        return;
                    }
                    break;
                case 0x205:  // 우클릭으로 닫기 (게임 메뉴의 우클릭 = ESC와 같게)
                    BeginInvoke((Action)CloseUi);
                    return;
                case 0x2A3:  // 마우스가 창을 벗어남
                    track = false;
                    SetHot("");
                    if (Overlay)
                        App.Ungrab(this);
                    return;
                case 0x200:
                case 0x201:
                case 0x202:
                    int lp = unchecked((int)(long)m.LParam);
                    int x = (short)(lp & 0xFFFF), y = (short)((lp >> 16) & 0xFFFF);
                    string act = "";
                    foreach (var it in Rects)
                        if (x >= it.r.X && x < it.r.X + it.r.Width && y >= it.r.Y && y < it.r.Y + it.r.Height)
                            act = it.act;
                    if (m.Msg == 0x200)
                    {
                        if (!track)  // 창을 벗어날 때 알림(WM_MOUSELEAVE)을 받게 한다
                        {
                            var tme = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = 2, hwnd = Handle };
                            track = TrackMouseEvent(ref tme);
                            if (Overlay)  // 게임 위 창에 마우스가 들어오면 포커스를 가져와 게임이 클릭을 받지 않게 한다
                                App.Grab(this);
                        }
                        SetHot(act);
                    }
                    else if (m.Msg == 0x201)
                    {
                        Down = act;
                        if (act == "")  // 빈 곳을 끌면 창 이동
                        {
                            ReleaseCapture();
                            PostMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);  // WM_NCLBUTTONDOWN, HTCAPTION
                        }
                    }
                    else if (act != "" && act == Down)
                        BeginInvoke((Action)(() => Act(act)));
                    return;
            }
            base.WndProc(ref m);
        }
    }

    // 결과 화면 버튼: 알약 모양을 그린 최상위 창 (포커스 안 뺏음). 클릭은 받는다
    sealed class ResultButton : Form
    {
        public ResultButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "와이프아웃 버튼";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08080000 | 0x8 | 0x80;  // LAYERED|NOACTIVATE, TOPMOST, TOOLWINDOW
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x201)  // WM_LBUTTONDOWN
            {
                BeginInvoke((Action)App.ShowWipe);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
