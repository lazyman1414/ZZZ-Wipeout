#Requires AutoHotkey v2.0
#SingleInstance Off

; 젠레스 존 제로 - 와이프아웃 캡처 (키보드 단축키 스크립트와 따로 동작)
; 마지막 적을 쓰러뜨리면 나오는 WIPEOUT 연출에서 장면마다 1장씩 찍어 임시 폴더에 두고,
; 도전 결과 화면의 "한 번 더" 왼쪽에 확인 버튼을 띄운다. 게임 창이 앞에 있을 때만 0.1초마다 화면을 본다
try TraySetIcon A_ScriptDir "\ZZZ-Wipeout.ico"
A_IconTip := "ZZZ 와이프아웃 캡처"
GAME := "ahk_exe ZenlessZoneZero.exe"

; 이미 떠 있으면 그 프로그램에 설정 창을 열라고 알리고 끝낸다 (로그인 자동 실행일 때는 조용히 끝낸다)
SHOW_MSG := DllCall("RegisterWindowMessage", "str", "ZZZ_WIPEOUT_SHOW", "uint")
FROM_LOGON := A_Args.Length && A_Args[1] = "--logon"
DetectHiddenWindows true
for hw in WinGetList(A_ScriptFullPath " ahk_class AutoHotkey")
    if WinGetPID(hw) != DllCall("GetCurrentProcessId") {
        if !FROM_LOGON
            PostMessage SHOW_MSG, 0, 0, , hw
        ExitApp
    }
DetectHiddenWindows false
OnMessage SHOW_MSG, (*) => (SetTimer(ShowSettings, -1), 0)

; 설정: 저장 폴더(비우면 사진\젠존제 와이프아웃), 자동 저장, 결과 화면 버튼 표시.
; 문서·사진 폴더는 Windows 보안이 막을 수 있어서 설정 파일은 AppData에 둔다
CFG_DIR := A_AppData "\젠존제 와이프아웃", CFG_INI := CFG_DIR "\설정.ini"
CFG := {dir: IniRead(CFG_INI, "설정", "저장폴더", ""), auto: IniRead(CFG_INI, "설정", "자동저장", 0) = 1
    , btn: IniRead(CFG_INI, "설정", "결과버튼", 1) = 1, debug: IniRead(CFG_INI, "설정", "디버그", 0) = 1
    , png: IniRead(CFG_INI, "설정", "PNG", 0) = 1, per: Max(1, Min(10, Integer(IniRead(CFG_INI, "설정", "장면당캡처", 3))))
    , rec: IniRead(CFG_INI, "설정", "녹화에보이기", 1) = 1}
; Windows 로그인 시 자동 실행(기본 꺼짐): 설정 창에서 켜면 현재 사용자 시작 프로그램(레지스트리 Run)에 등록한다. 관리자 권한은 필요 없다
RUN_KEY := "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
RunCmd() => '"' A_AhkPath '" "' A_ScriptFullPath '" --logon'
BootOn() => RegRead(RUN_KEY, "ZZZ-Wipeout", "") != ""
BootSet(on) {
    try on ? RegWrite(RunCmd(), "REG_SZ", RUN_KEY, "ZZZ-Wipeout") : RegDelete(RUN_KEY, "ZZZ-Wipeout")
}
FIRST_RUN := !FileExist(CFG_INI)
if BootOn()  ; 켜 둔 상태에서 폴더를 옮겼으면 경로를 새로 고친다
    BootSet(true)

; 트레이 아이콘: 더블클릭 = 설정, 우클릭 = 메뉴
A_TrayMenu.Delete()
A_TrayMenu.Add("설정", ShowSettings)
A_TrayMenu.Add("최근 캡처 보기", ShowWipe)
A_TrayMenu.Add("저장 폴더 열기", WipeOpenDir)
A_TrayMenu.Add()
A_TrayMenu.Add("종료", (*) => ExitApp())
A_TrayMenu.Default := "설정"
DllCall("QueryPerformanceFrequency", "int64*", &QPF := 0)
DllCall("LoadLibrary", "str", "gdiplus", "ptr")
NumPut("uint", 1, GDIP_SI := Buffer(24, 0))
DllCall("gdiplus\GdiplusStartup", "ptr*", &GDIP := 0, "ptr", GDIP_SI, "ptr", 0)

; 도전 결과 화면은 우측 하단 "완료" 글자로 알아본다
DONE := {name: "완료", click: [0.8850, 0.9529]
    , w: "0.8738,0.9549 0.8758,0.9542 0.8770,0.9514 0.8777,0.9611 0.8797,0.9458 0.8820,0.9563 0.8832,0.9458 0.8840,0.9486 0.8887,0.9507 0.8895,0.9590 0.8910,0.9486 0.8926,0.9486 0.8938,0.9576 0.8953,0.9493"
    , d: "0.8703,0.9361 0.8711,0.9535 0.8723,0.9375 0.8734,0.9396 0.8766,0.9667 0.8805,0.9660 0.8844,0.9674 0.8867,0.9639 0.8898,0.9389 0.8930,0.9396 0.8961,0.9403 0.8984,0.9500 0.8992,0.9674 0.9004,0.9514"}
DONE.pts := ParsePts(DONE.w, true, ParsePts(DONE.d, false))

; 와이프아웃 캡처: 마지막 적을 쓰러뜨리면 화면이 노랗게 바뀌고 WIPEOUT 글자와 함께 장면 몇 개(보내주신 영상은 3개)가
; 잠깐씩 멈춘다. 장면 사이엔 화면이 회색으로 번쩍이고, 멈춘 장면도 뒤로 갈수록 가로로 찢어지는 지지직이 생긴다.
; 화면을 128x72로 줄여(평균) 화면의 40% 이상이 노랗고 채도가 높고 직전과 거의 같은 순간이 두 번 이어지면 멈춘 장면으로 보고,
; 같은 장면 중 세로 변화량/가로 변화량(가로로 찢어지면 커짐)이 가장 작은 순간을 원본 크기로 찍어 둔다.
; 기준값은 보내주신 영상(2560x1440)에서 뽑음. 끝나면 임시 폴더에 PNG로 저장하고, 도전 결과 화면에 확인 버튼을 띄운다
WIPE := {on: false, ready: false, seen: false, files: [], dir: A_Temp "\젠존제_와이프아웃", log: A_Temp "\젠존제_와이프아웃.log"
    , W: 128, H: 72, ratio: 9 / 16, stamp: "", endTick: 0, note: "", groups: []}
; 결과 화면 버튼: 알약 모양을 그린 최상위 창 (포커스 안 뺏음, 화면 캡처에서 빠짐). 클릭은 받는다
WIPE_BTN := Gui("+AlwaysOnTop -Caption +ToolWindow -DPIScale +E0x08080000")  ; LAYERED|NOACTIVATE
WIPE_BTN.Show("NA Hide w1 h1")
; 녹화에 보이기(설정)를 끄면 화면 캡처·녹화에서 뺀다. 이 버튼은 판별 지점(위쪽 줄, "완료" 글자)과 겹치지 않아 켜도 판별에 지장 없다
UiCapture(hwnd) => DllCall("SetWindowDisplayAffinity", "ptr", hwnd, "uint", CFG.rec ? 0 : 0x11)
UiCapture(WIPE_BTN.Hwnd)
OnMessage 0x0201, (wp, lp, msg, hwnd) => hwnd = WIPE_BTN.Hwnd ? (SetTimer(ShowWipe, -1), 0) : ""  ; WM_LBUTTONDOWN

; 주황~노랑(색상 약 25~55도)이고 채도가 높으면 노란색. 맵이 어두우면 노란 화면도 어두워서 밝기는 낮게(R>90) 본다
WipeYellow(c) => (r := (c >> 16) & 0xFF) > 90 && r - (c & 0xFF) > r * 0.45 && (g := (c >> 8) & 0xFF) > r * 0.45 && g < r * 0.95

; 0.1초마다: 화면 위쪽(0.06h) 한 줄의 64곳 중 절반 이상이 노란색이면 와이프아웃 시작으로 본다.
; 4분의 1 이상이면 판별 기준을 맞출 수 있게 색을 로그에 남긴다
WipeTop(c) {
    static logged := 0
    try WinGetClientPos(&gx, &gy, &w, &h, GAME)
    catch
        return false
    buf := GrabLum(gx, gy + Round(h * 0.06), w, 1), n := 0
    loop 64
        n += WipeYellow(NumGet(buf, Floor(w * (A_Index - 0.5) / 64) * 4, "uint"))
    if n >= 16 && A_TickCount - logged > 500 {
        logged := A_TickCount, cols := ""
        loop 8
            cols .= Format(" {:06X}", NumGet(buf, Floor(w * (A_Index - 0.5) / 8) * 4, "uint") & 0xFFFFFF)
        WipeLog(Format("위쪽 노란색 {}/64, 커서 {}, 색{}", n, c ? "보임" : "숨김", cols))
    }
    return n >= 32
}

; 같은 내용은 연달아 남기지 않는다 (0.1초마다 같은 오류가 반복되는 경우)
WipeLog(s) {
    static last := ""
    if s = last
        return
    last := s
    try FileAppend FormatTime(, "HH:mm:ss.") A_MSec " " s "`n", WIPE.log, "UTF-8"
}

StartWipe() {
    WipeLog("와이프아웃 시작")
    WIPE.on := true, WIPE.prev := 0, WIPE.okPrev := false, WIPE.scenes := [], WIPE.work := 0
    WIPE.start := WIPE.lastYel := A_TickCount, WIPE.ticks := 0, WIPE.ms := 0, WIPE.trace := ""
    WIPE.dbg := [], WIPE.dbgCsv := "판별,시각ms,노랑,채도,차이,찢어짐,줄무늬,지글거림,상태,장면`n"
    SetTimer WipeTick, 30
}

; 매번 원본 크기로 한 번 찍고 그 그림을 메모리에서 줄여 판별한다. 후보로 고르면 판별한 그 그림을 그대로 남긴다
; (화면에서 바로 줄이면 느리고, 후보를 다시 찍으면 그사이 화면이 바뀌어 짧은 장면을 놓쳤음)
WipeTick() {
    DllCall("QueryPerformanceCounter", "int64*", &t0 := 0)
    try WinGetClientPos(&gx, &gy, &w, &h, GAME)
    catch
        return EndWipe()
    WIPE.work := GrabFull(gx, gy, w, h, WIPE.work)
    s := WipeStats(WipeShrink(WIPE.work, w, h), WIPE.prev)
    WIPE.prev := s.L
    ; 노랗고 채도가 높고 직전과 크게 다르지 않은(지지직·번쩍임은 차이 13 이상) 화면은 모두 장면 후보로 묶는다.
    ; 그중 직전과 거의 같은(차이 5 미만, 머리카락 등이 조금 움직임) 화면이 두 번 이어지면 멈춘 장면
    cand := s.yel >= 0.4 && s.sat >= 60 && s.diff < 12
    ; 마지막 장면은 검은 화면과 번갈아 깜빡이며 끝나서 직전 화면과는 늘 크게 다르다. 그래도 그 장면과 비슷하면 후보로 넣는다
    if !cand && s.yel >= 0.4 && s.sat >= 60 && WIPE.scenes.Length && WipeDiff(s.L, WIPE.scenes[-1].sig) < 15
        cand := true
    ok := cand && s.diff < 5, stable := ok && WIPE.okPrev
    WIPE.trace .= flag := stable ? "S" : cand ? "c" : s.yel >= 0.4 ? "y" : "."
    scan := jag := ""
    if CFG.debug && s.yel >= 0.4  ; 진단: 노란 화면은 가운데 절반을 원본 크기로 남긴다 (끝난 뒤 저장)
        WipeDebugKeep(w, h)
    if cand {
        sc := WIPE.scenes.Length ? WIPE.scenes[-1] : 0
        if (!sc || WipeDiff(s.L, sc.sig) > 15) && WIPE.scenes.Length < 12  ; 마지막 장면과 많이 다르면 새 장면
            WIPE.scenes.Push(sc := {shots: [], keep: false, stab: false, cnt: 0, maxSat: 0, jags: [], tears: ""})
        ; 점수(낮을수록 깨끗함) = 지글거림(jag) + 색 빠짐 벌점(채도 100 미만) + 가로 줄무늬 조금 + 멈춤이 아니면 아주 조금.
        ; 멈춤 여부는 거의 안 본다: 색이 서서히 돌아오는 깨끗한 장면은 멈춤으로 안 잡히고, 처음부터 지글거리는 장면은 멈춤으로 잡혔음
        jag := WipeJag(WIPE.work, w, h), scan := WipeScan(WIPE.work, w, h)
        score := jag + Max(0, 100 - s.sat) * 0.004 + scan * 0.002 + (stable ? 0 : 0.01)
        sc.sig := s.L, sc.cnt++, sc.stab := sc.stab || stable, sc.maxSat := Max(sc.maxSat, s.sat), sc.jags.Push(jag), sc.tears .= Format(" {}{:.3f}", stable ? "" : "~", score)
        ; 장면마다 점수가 좋은 장면당 캡처 수(설정, 기본 3)만큼 보관한다. 꽉 차면 가장 나쁜 것과 바꾸고, 밀려난 그림은 다음 판별에 다시 쓴다
        if sc.shots.Length < CFG.per
            sc.shots.Push({hbm: WIPE.work, score: score}), WIPE.work := 0, WIPE.ratio := h / w
        else {
            wi := 1
            for i, sh in sc.shots
                if sh.score > sc.shots[wi].score
                    wi := i
            if score < sc.shots[wi].score
                t := sc.shots[wi].hbm, sc.shots[wi] := {hbm: WIPE.work, score: score}, WIPE.work := t
        }
    }
    WIPE.okPrev := ok
    if CFG.debug
        WIPE.dbgCsv .= Format("{},{},{:.2f},{:.0f},{:.1f},{:.3f},{},{},{},{}`n", WIPE.ticks, A_TickCount - WIPE.start
            , s.yel, s.sat, s.diff, s.tear, scan = "" ? "" : Round(scan, 1), jag = "" ? "" : Round(jag, 3), flag, cand ? WIPE.scenes.Length : "")
    if s.yel >= 0.2
        WIPE.lastYel := A_TickCount
    DllCall("QueryPerformanceCounter", "int64*", &t1 := 0)
    WIPE.ticks++, WIPE.ms += (t1 - t0) * 1000 / QPF
    ; 노란 화면이 1초 넘게 안 보이면(검은 화면·결과 화면) 끝. 시작 직후 전환 효과는 노란 부분이 적어서 1.5초까지는 기다린다.
    ; 노란 맵을 잘못 본 경우를 위해 15초에서도 끝
    if A_TickCount - WIPE.lastYel > 1000 && A_TickCount - WIPE.start > 1500 || A_TickCount - WIPE.start > 15000
        EndWipe()
}

; 장면 사이엔 시간이 없어서 원본은 메모리에 두었다가 끝난 뒤에 PNG로 저장한다
EndWipe() {
    SetTimer WipeTick, 0
    WIPE.on := false
    if WIPE.work
        DllCall("DeleteObject", "ptr", WIPE.work), WIPE.work := 0
    if CFG.debug
        WipeDebugSave()
    ; 각 장면 앞에는 캐릭터가 회색으로 바랜 전환 화면이 잠깐 나온다(진단 자료: 화면 채도 72~80, 실제 장면은 102~141).
    ; 그래서 후보 2번 이상이고 채도가 90 이상까지 올라간 장면을 모두 남긴다. 2개가 안 되면 나머지 장면 중 점수가 좋은 것으로 채운다
    rest := [], need := 2
    for sc in WIPE.scenes {
        sc.med := WipeMedian(sc.jags), sc.best := 9
        for sh in sc.shots
            sc.best := Min(sc.best, sh.score)
        ; 회색 전환 화면은 후보 1~2번에 멈춤이 없다. 실제 장면이라도 맵에 따라 채도가 79에 그칠 때가 있어서
        ; 채도가 90 이상이거나, 멈춤이 있었거나, 후보가 3번 이상이면 남긴다
        if sc.cnt >= 2 && (sc.maxSat >= 90 || sc.stab || sc.cnt >= 3)
            sc.keep := true, need--
        else if sc.shots.Length
            rest.Push(sc)
    }
    loop Max(0, need) {
        bi := 0
        for i, sc in rest
            if !bi || sc.best < rest[bi].best
                bi := i
        if bi
            rest.RemoveAt(bi).keep := true
    }
    groups := [], total := 0
    for sc in WIPE.scenes {
        if sc.keep {  ; 점수가 좋은 순서로
            g := sc.shots.Clone(), n := g.Length
            loop n - 1 {
                i := A_Index
                loop n - i
                    if g[A_Index].score > g[A_Index + 1].score
                        t := g[A_Index], g[A_Index] := g[A_Index + 1], g[A_Index + 1] := t
            }
            groups.Push(g), total += n
        } else
            for sh in sc.shots
                DllCall("DeleteObject", "ptr", sh.hbm)
    }
    log := Format("{} 와이프아웃 {:.1f}초, 판별 {}회 평균 {:.1f}ms, 장면 {}개 중 {}개 {}장 저장`n  판별: {}`n", FormatTime(, "HH:mm:ss")
        , (A_TickCount - WIPE.start) / 1000, WIPE.ticks, WIPE.ms / Max(WIPE.ticks, 1), WIPE.scenes.Length, groups.Length, total, WIPE.trace)
    for sc in WIPE.scenes
        log .= Format("  장면 {}{}: 최대 채도 {:.0f}, 지글거림 중앙값 {:.3f}, 가장 좋은 점수 {:.3f} / 후보(~는 멈춤 아님){}`n"
            , A_Index, sc.keep ? "" : " (버림)", sc.maxSat, sc.med, sc.best, sc.tears)
    try FileAppend log, WIPE.log, "UTF-8"
    if !groups.Length {  ; 하나도 못 찍었으면 이전 캡처를 그대로 둔다
        WIPE.scenes := []
        return
    }
    WipeViewClose()
    try DirDelete WIPE.dir, true
    DirCreate WIPE.dir
    files := [], WIPE.groups := []
    for gi, g in groups {  ; 파일 이름: 장면-순번 (1-1이 그 장면에서 가장 깨끗함)
        row := []
        for j, sh in g {
            f := Format("{}\{}-{}{}", WIPE.dir, gi, j, CFG.png ? ".png" : ".jpg")
            if SavePng(sh.hbm, f)
                files.Push(f), row.Push(files.Length)
            DllCall("DeleteObject", "ptr", sh.hbm)
        }
        if row.Length
            WIPE.groups.Push(row)
    }
    WIPE.scenes := [], WIPE.prev := 0
    WIPE.files := files, WIPE.stamp := FormatTime(, "yyyyMMdd_HHmmss"), WIPE.endTick := A_TickCount
    WIPE.ready := files.Length > 0, WIPE.seen := false, WIPE.note := ""
    if CFG.auto && files.Length {  ; 자동 저장: 확인 창을 열면 결과 문구가 보인다
        WIPE.note := "자동 " WipeSave(0)
        WipeLog(WIPE.note)
    }
}

; 진단 모드(설정.ini의 디버그=1): 노란 화면마다 가운데 절반(2560x1440이면 1280x720)을 압축 없이 원본 크기로 보관한다.
; 캐릭터 가장자리의 1~2px 지글거림까지 그대로 남겨 판별 기준을 실제 게임 화면으로 맞추는 데 쓴다 (최대 70장)
WipeDebugKeep(w, h) {
    if WIPE.dbg.Length >= 70
        return
    cw := w // 2, ch := h // 2
    bi := Buffer(40, 0), NumPut("uint", 40, "int", cw, "int", -ch, "ushort", 1, "ushort", 32, bi)
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    src := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr"), so := DllCall("SelectObject", "ptr", src, "ptr", WIPE.work, "ptr")
    dst := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    hbm := DllCall("CreateDIBSection", "ptr", dst, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    do := DllCall("SelectObject", "ptr", dst, "ptr", hbm, "ptr")
    DllCall("BitBlt", "ptr", dst, "int", 0, "int", 0, "int", cw, "int", ch, "ptr", src, "int", w // 4, "int", h // 4, "uint", 0x00CC0020)
    DllCall("SelectObject", "ptr", dst, "ptr", do), DllCall("DeleteDC", "ptr", dst)
    DllCall("SelectObject", "ptr", src, "ptr", so), DllCall("DeleteDC", "ptr", src)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    WIPE.dbg.Push({hbm: hbm, tick: WIPE.ticks})
}

; 진단 자료를 %TEMP%\젠존제_와이프아웃_진단에 저장한다: 판별 번호.png와 판별마다의 수치(수치.csv)
WipeDebugSave() {
    dir := A_Temp "\젠존제_와이프아웃_진단"
    try DirDelete dir, true
    DirCreate dir
    try FileAppend WIPE.dbgCsv, dir "\수치.csv", "UTF-8"
    for d in WIPE.dbg {
        SavePng(d.hbm, Format("{}\{:03}.png", dir, d.tick))
        DllCall("DeleteObject", "ptr", d.hbm)
    }
    WIPE.dbg := []
}

; 찍어 둔 원본 그림(cw x ch)을 128x72로 평균 내어 줄인다 (HALFTONE. 메모리 안에서라 2560x1440도 약 5ms)
WipeShrink(hbm, cw, ch) {
    W := WIPE.W, H := WIPE.H
    bi := Buffer(40, 0), NumPut("uint", 40, "int", W, "int", -H, "ushort", 1, "ushort", 32, bi)
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    src := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    sold := DllCall("SelectObject", "ptr", src, "ptr", hbm, "ptr")
    mdc := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    dib := DllCall("CreateDIBSection", "ptr", mdc, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    old := DllCall("SelectObject", "ptr", mdc, "ptr", dib, "ptr")
    DllCall("SetStretchBltMode", "ptr", mdc, "int", 4)
    DllCall("SetBrushOrgEx", "ptr", mdc, "int", 0, "int", 0, "ptr", 0)
    DllCall("StretchBlt", "ptr", mdc, "int", 0, "int", 0, "int", W, "int", H, "ptr", src, "int", 0, "int", 0, "int", cw, "int", ch, "uint", 0x00CC0020)
    DllCall("GdiFlush")
    buf := Buffer(W * H * 4)
    DllCall("RtlMoveMemory", "ptr", buf, "ptr", bits, "uptr", buf.Size)
    DllCall("SelectObject", "ptr", mdc, "ptr", old)
    DllCall("DeleteObject", "ptr", dib)
    DllCall("DeleteDC", "ptr", mdc)
    DllCall("SelectObject", "ptr", src, "ptr", sold)
    DllCall("DeleteDC", "ptr", src)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    return buf
}

; 줄인 화면의 밝기(L), 화면 전체 노란 비율(위쪽이 흰 건물인 장면도 있어 전체를 봄), 평균 채도, 세로/가로 변화량 비(tear), 직전 밝기와의 평균 차이(diff)
WipeStats(buf, prev) {
    W := WIPE.W, H := WIPE.H, L := Buffer(W * H), yel := 0, sat := 0, v := 0, hz := 0, diff := 0, k := 0
    loop H {
        y := A_Index - 1
        loop W {
            c := NumGet(buf, k * 4, "uint"), r := (c >> 16) & 0xFF, g := (c >> 8) & 0xFF, b := c & 0xFF
            lum := (r * 77 + g * 150 + b * 29) >> 8  ; 변수 이름은 대소문자를 안 가려서 L과 다른 이름으로
            NumPut("uchar", lum, L, k)
            sat += Max(r, g, b) - Min(r, g, b)
            if WipeYellow(c)
                yel++
            if A_Index > 1
                hz += Abs(lum - NumGet(L, k - 1, "uchar"))
            if y
                v += Abs(lum - NumGet(L, k - W, "uchar"))
            if prev
                diff += Abs(lum - NumGet(prev, k, "uchar"))
            k++
        }
    }
    return {L: L, yel: yel / (W * H), sat: sat / (W * H), tear: v / Max(hz, 1), diff: prev ? diff / (W * H) : 99}
}

; 원본 그림의 가로 줄무늬(스캔라인) 세기: 64x40 지점에서 초록 값의 위아래 2차 차이(2p - 위 - 아래) 평균.
; 와이프아웃 화면엔 한 줄 간격 줄무늬가 깔리고 세기가 순간마다 달라서(직접 찍은 화면은 자동 캡처의 절반쯤),
; 같은 장면 중 가장 옅은 순간을 고르는 데 쓴다. 128x72로 줄인 화면에서는 줄무늬가 평균에 묻혀 안 보인다
WipeScan(hbm, w, h) {
    bm := Buffer(32, 0), DllCall("GetObject", "ptr", hbm, "int", bm.Size, "ptr", bm)
    bits := NumGet(bm, A_PtrSize = 8 ? 24 : 20, "ptr"), stride := w * 4, s := 0
    loop 40 {
        row := bits + Floor(h * (A_Index - 0.5) / 40) * stride
        loop 64 {
            p := row + Floor(w * (A_Index - 0.5) / 64) * 4
            s += Abs(2 * NumGet(p, 1, "uchar") - NumGet(p - stride, 1, "uchar") - NumGet(p + stride, 1, "uchar"))
        }
    }
    return s / 2560
}

; 캐릭터 가장자리 지글거림: 화면 가운데 절반에서 일정 간격(2560 폭이면 8px)마다, 가로로 2px 옆과 밝기가 크게 다른 곳(세로 가장자리)만 골라
; 위아래 2줄 차이 / 가로 차이의 비를 낸다. 깨끗한 가장자리는 위아래로 이어져서 작고, 줄마다 옆으로 밀리면 커진다.
; 2줄 차이를 써서 한 줄 간격 가로 줄무늬는 상쇄된다 (진단 자료: 깨끗함 0.35~0.44, 지글거림 0.6~0.8)
WipeJag(hbm, w, h) {
    bm := Buffer(32, 0), DllCall("GetObject", "ptr", hbm, "int", bm.Size, "ptr", bm)
    bits := NumGet(bm, A_PtrSize = 8 ? 24 : 20, "ptr"), stride := w * 4, st := Max(4, w // 320), ev := eh := 0
    y := h // 4
    while y < h * 3 // 4 {
        row := bits + y * stride, x := w // 4
        while x < w * 3 // 4 {
            p := row + x * 4, a := NumGet(p, 1, "uchar"), dh := Abs(a - NumGet(p, 9, "uchar"))
            if dh > 24
                eh += dh, ev += Abs(a - NumGet(p + 2 * stride, 1, "uchar"))
            x += st
        }
        y += st
    }
    return eh ? ev / eh : 0
}

WipeMedian(a) {
    if !a.Length
        return 9
    b := a.Clone(), n := b.Length
    loop n - 1 {  ; 후보가 몇십 개뿐이라 단순 정렬
        i := A_Index
        loop n - i
            if b[A_Index] > b[A_Index + 1]
                t := b[A_Index], b[A_Index] := b[A_Index + 1], b[A_Index + 1] := t
    }
    return Mod(n, 2) ? b[(n + 1) // 2] : (b[n // 2] + b[n // 2 + 1]) / 2
}

WipeDiff(a, b) {
    d := 0
    loop n := a.Size
        d += Abs(NumGet(a, A_Index - 1, "uchar") - NumGet(b, A_Index - 1, "uchar"))
    return d / n
}

; 게임 화면을 원본 크기로 찍어 비트맵 핸들을 돌려준다. hbm을 주면 그 비트맵에 다시 찍는다
GrabFull(x, y, w, h, hbm := 0) {
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    mdc := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    if !hbm {
        bi := Buffer(40, 0), NumPut("uint", 40, "int", w, "int", -h, "ushort", 1, "ushort", 32, bi)
        hbm := DllCall("CreateDIBSection", "ptr", mdc, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    }
    old := DllCall("SelectObject", "ptr", mdc, "ptr", hbm, "ptr")
    DllCall("BitBlt", "ptr", mdc, "int", 0, "int", 0, "int", w, "int", h, "ptr", sdc, "int", x, "int", y, "uint", 0x00CC0020)
    DllCall("GdiFlush")
    DllCall("SelectObject", "ptr", mdc, "ptr", old)
    DllCall("DeleteDC", "ptr", mdc)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    return hbm
}

; 파일 이름이 .jpg면 품질 95 JPG(2560x1440 기준 PNG의 4분의 1 안팎), 아니면 무손실 PNG로 저장
SavePng(hbm, file) {
    jpg := SubStr(file, -4) = ".jpg", params := 0
    clsid := Buffer(16), DllCall("ole32\CLSIDFromString", "wstr", jpg ? "{557CF401-1A04-11D3-9A73-0000F81EF32E}" : "{557CF406-1A04-11D3-9A73-0000F81EF32E}", "ptr", clsid)
    if jpg {  ; EncoderParameters 1개: 품질(Quality GUID, ULONG 1개)
        q := Buffer(4), NumPut("uint", 95, q)
        params := Buffer(8 + 16 + 8 + A_PtrSize, 0), NumPut("uint", 1, params)
        DllCall("ole32\CLSIDFromString", "wstr", "{1D5BE4B5-FA4A-452D-9CDD-5DB35105E7EB}", "ptr", params.Ptr + A_PtrSize)
        NumPut("uint", 1, "uint", 4, "ptr", q.Ptr, params, A_PtrSize + 16)  ; 값 1개, EncoderParameterValueTypeLong
    }
    DllCall("gdiplus\GdipCreateBitmapFromHBITMAP", "ptr", hbm, "ptr", 0, "ptr*", &img := 0)
    r := DllCall("gdiplus\GdipSaveImageToFile", "ptr", img, "wstr", file, "ptr", clsid, "ptr", params)
    DllCall("gdiplus\GdipDisposeImage", "ptr", img)
    return !r
}

; 와이프아웃을 찍은 뒤 도전 결과 화면("완료" 글자)이 보이면 버튼을 띄운다.
; 결과 화면을 한 번 보고 벗어나면 버튼을 끝낸다 (결과 화면이 2분 안에 안 나와도 끝)
WipeButton() {
    if !WIPE.ready
        return
    if !CFG.btn  ; 설정에서 끄면 버튼을 띄우지 않는다 (캡처와 자동 저장은 그대로)
        return WIPE_BTN.Hide()
    static last := -1, same := 0
    if ResultScreen() {
        WIPE.seen := true
        if DllCall("IsWindowVisible", "ptr", WIPE_BTN.Hwnd)
            return
        ; 버튼 줄이 다 나타날 때까지 기다린다: 가장 왼쪽 끝이 0.1초 간격으로 세 번 같으면 그 왼쪽에 띄운다
        left := WipeBtnLeft()
        same := Abs(left - last) < 0.005 ? same + 1 : 0, last := left
        if same >= 2
            WipeBtnShow(left)
        return
    }
    last := -1, same := 0
    WIPE_BTN.Hide()
    if WIPE.seen && WV.gui && WV.overlay  ; "한 번 더"·"완료"로 결과 화면을 벗어나면 게임 위에 띄운 확인 창도 닫는다
        WipeViewClose()
    if WIPE.seen || A_TickCount - WIPE.endTick > 120000
        WIPE.ready := false
}

; 결과 화면 아래 줄에서 게임이 그린 것(버튼, "한 번 더" 왼쪽에 붙는 배터리 칸 등) 중 가장 왼쪽 끝을 창 폭 비율로 돌려준다.
; 결과마다 왼쪽에 붙는 것이 달라서 매번 잰다. 바탕은 검정(밝기 0), 버튼은 테두리·글자가 밝고 속은 위아래로 밝기가 달라서
; 버튼 높이(0.932~0.977h)를 세로로 훑은 최댓값이 20을 넘으면 무언가 있는 칸으로 본다.
; "완료" 버튼 안(0.88w)에서 왼쪽으로 따라가다 빈 칸이 0.03w보다 길게 이어지면(버튼 간격은 0.0115w) 멈춘다
WipeBtnLeft() {
    try WinGetClientPos(&gx, &gy, &w, &h, GAME)
    catch
        return 0.6355
    rows := Round(h * 0.045), buf := GrabLum(gx, gy + Round(h * 0.932), w, rows), left := 0.88, dark := 0, x := 0.88
    while (x -= 0.002) > 0.02 {
        m := 0, col := Floor(w * x)
        loop rows // 2 {
            c := NumGet(buf, ((A_Index - 1) * 2 * w + col) * 4, "uint")
            m := Max(m, ((c >> 16 & 0xFF) * 77 + (c >> 8 & 0xFF) * 150 + (c & 0xFF) * 29) >> 8)
        }
        if m > 20
            left := x, dark := 0
        else if (dark += 0.002) > 0.03
            break
    }
    return left
}

; 게임 버튼 줄의 가장 왼쪽(left, 창 폭 비율) 바로 왼쪽에 "한 번 더"와 같은 크기·같은 줄로 그린다
; (2000x1125 결과 화면 캡처에서 잰 값: 한 번 더 0.6355~0.7945w, 0.9271~0.9787h, 버튼 간격 0.0115w)
WipeBtnShow(left := 0.6355) {
    try WinGetClientPos(&gx, &gy, &w, &h, GAME)
    catch
        return
    bw := Round(w * 0.159), bh := Round(h * 0.0533), y := gy + Round(h * 0.9271)
    x := gx + Max(Round(w * 0.01), Round(w * (left - 0.0115)) - bw)
    bi := Buffer(40, 0), NumPut("uint", 40, "int", bw, "int", -bh, "ushort", 1, "ushort", 32, bi)
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    mdc := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    dib := DllCall("CreateDIBSection", "ptr", mdc, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    old := DllCall("SelectObject", "ptr", mdc, "ptr", dib, "ptr")
    DllCall("gdiplus\GdipCreateBitmapFromScan0", "int", bw, "int", bh, "int", bw * 4, "int", 0xE200B, "ptr", bits, "ptr*", &gb := 0)
    DllCall("gdiplus\GdipGetImageGraphicsContext", "ptr", gb, "ptr*", &g := 0)
    WipeBtnPaint(g, bw, bh, h, "와이프아웃 " WIPE.files.Length "장")
    DllCall("gdiplus\GdipDeleteGraphics", "ptr", g)
    DllCall("gdiplus\GdipDisposeImage", "ptr", gb)
    pt := Buffer(8), NumPut("int", x, "int", y, pt)
    sz := Buffer(8), NumPut("int", bw, "int", bh, sz)
    blend := Buffer(4), NumPut("uchar", 0, "uchar", 0, "uchar", 255, "uchar", 1, blend)  ; AC_SRC_OVER, 255, AC_SRC_ALPHA
    DllCall("UpdateLayeredWindow", "ptr", WIPE_BTN.Hwnd, "ptr", sdc, "ptr", pt, "ptr", sz, "ptr", mdc, "ptr", Buffer(8, 0)
        , "uint", 0, "ptr", blend, "uint", 2)  ; ULW_ALPHA
    DllCall("SelectObject", "ptr", mdc, "ptr", old)
    DllCall("DeleteObject", "ptr", dib)
    DllCall("DeleteDC", "ptr", mdc)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    if WinActive(GAME)
        DllCall("ShowWindow", "ptr", WIPE_BTN.Hwnd, "int", 4)  ; SW_SHOWNOACTIVATE
}

; 게임 결과 화면 버튼 모양 (2000x1125 캡처에서 잰 값): 알약형, 테 두께 0.0053h(위 밝기 73 → 아래 48),
; 검은 바탕에 가는 대각선 격자 무늬(밝기 0~18), 흰 글자는 굵고 오른쪽으로 기울어짐(높이 0.022h).
; 게임 글꼴은 게임 파일 안에 있어 쓸 수 없어서 설치된 굵은 한글 글꼴로 비슷하게 그린다
; (x, y)에 그린다. hot이면(마우스가 올라가 있으면) 테를 노랗게
WipeBtnPaint(g, bw, bh, h, text, fontName := "Noto Sans KR Black", x := 0, y := 0, hot := false) {
    t := h * 0.0045, d := bh - t  ; 테 두께, 테 가운데 선 기준 반원 지름
    DllCall("gdiplus\GdipSetSmoothingMode", "ptr", g, "int", 4)
    DllCall("gdiplus\GdipSetTextRenderingHint", "ptr", g, "int", 4)  ; 투명 바탕이라 ClearType 대신 안티에일리어싱
    DllCall("gdiplus\GdipCreatePath", "int", 0, "ptr*", &path := 0)
    DllCall("gdiplus\GdipAddPathArc", "ptr", path, "float", x + t / 2, "float", y + t / 2, "float", d, "float", d, "float", 90, "float", 180)
    DllCall("gdiplus\GdipAddPathArc", "ptr", path, "float", x + bw - t / 2 - d, "float", y + t / 2, "float", d, "float", d, "float", 270, "float", 180)
    DllCall("gdiplus\GdipClosePathFigure", "ptr", path)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", 0xFF020202, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", path)
    DllCall("gdiplus\GdipCreateHatchBrush", "int", 5, "uint", 0xFF0D0D0D, "uint", 0xFF020202, "ptr*", &hatch := 0)  ; DiagonalCross
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", hatch, "ptr", path)
    p := Buffer(16), NumPut("float", 0, "float", y, "float", 0, "float", y + bh, p)
    DllCall("gdiplus\GdipCreateLineBrush", "ptr", p, "ptr", p.Ptr + 8, "uint", hot ? 0xFFFFD040 : 0xFF444444, "uint", hot ? 0xFFD49A1A : 0xFF2C2C2C, "int", 0, "ptr*", &lb := 0)
    DllCall("gdiplus\GdipCreatePen2", "ptr", lb, "float", t, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", path)
    WipeText(g, text, x, y, bw, bh, h * 0.024, 0xFFFFFFFF, 1, 0.2, fontName)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", lb)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", hatch)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeletePath", "ptr", path)
}

; 글자를 (x, y, w, h) 칸에 그린다. align 0 왼쪽·1 가운데·2 오른쪽, px 글자 크기(픽셀), style 1 굵게,
; trim 5면 칸을 넘는 경로를 가운데를 "…"로 줄인다.
; 게임 글자처럼 기울이려고 기울임 글꼴 대신 가로로 기울이는 변환(x' = x - sh·y)을 걸고, 칸 가운데가 그대로 있게 옮긴다
WipeText(g, text, x, y, w, h, px, color, align := 1, sh := 0.2, fontName := "Noto Sans KR Black", style := 1, trim := 0) {
    DllCall("gdiplus\GdipCreateFontFamilyFromName", "wstr", fontName, "ptr", 0, "ptr*", &fam := 0)
    if !fam
        DllCall("gdiplus\GdipCreateFontFamilyFromName", "wstr", "Malgun Gothic", "ptr", 0, "ptr*", &fam := 0)
    DllCall("gdiplus\GdipCreateFont", "ptr", fam, "float", px, "int", style, "int", 2, "ptr*", &font := 0)
    DllCall("gdiplus\GdipCreateStringFormat", "int", 0x1000, "int", 0, "ptr*", &fmt := 0)  ; 줄바꿈 안 함
    DllCall("gdiplus\GdipSetStringFormatAlign", "ptr", fmt, "int", align)
    DllCall("gdiplus\GdipSetStringFormatLineAlign", "ptr", fmt, "int", 1)
    if trim
        DllCall("gdiplus\GdipSetStringFormatTrimming", "ptr", fmt, "int", trim)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", color, "ptr*", &br := 0)
    DllCall("gdiplus\GdipCreateMatrix2", "float", 1, "float", 0, "float", -sh, "float", 1, "float", sh * (y + h / 2), "float", 0, "ptr*", &m := 0)
    DllCall("gdiplus\GdipSetWorldTransform", "ptr", g, "ptr", m)
    rc := Buffer(16), NumPut("float", x, "float", y, "float", w, "float", h, rc)
    DllCall("gdiplus\GdipDrawString", "ptr", g, "wstr", text, "int", -1, "ptr", font, "ptr", rc, "ptr", fmt, "ptr", br)
    DllCall("gdiplus\GdipResetWorldTransform", "ptr", g)
    DllCall("gdiplus\GdipDeleteMatrix", "ptr", m)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeleteStringFormat", "ptr", fmt)
    DllCall("gdiplus\GdipDeleteFont", "ptr", font)
    DllCall("gdiplus\GdipDeleteFontFamily", "ptr", fam)
}

WipeRoundRect(x, y, w, h, r) {
    DllCall("gdiplus\GdipCreatePath", "int", 0, "ptr*", &p := 0)
    for a in [[x, y, 180], [x + w - 2 * r, y, 270], [x + w - 2 * r, y + h - 2 * r, 0], [x, y + h - 2 * r, 90]]
        DllCall("gdiplus\GdipAddPathArc", "ptr", p, "float", a[1], "float", a[2], "float", 2 * r, "float", 2 * r, "float", a[3], "float", 90)
    DllCall("gdiplus\GdipClosePathFigure", "ptr", p)
    return p
}

; 기울어진 띠(평행사변형) 칠하기: (x, y)부터 너비 w·높이 h, 위쪽이 k만큼 오른쪽으로 밀림
WipeSlant(g, x, y, w, h, k, color) {
    pts := Buffer(32), NumPut("float", x + k, "float", y, "float", x + k + w, "float", y, "float", x + w, "float", y + h, "float", x, "float", y + h, pts)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", color, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPolygon", "ptr", g, "ptr", br, "ptr", pts, "int", 4, "int", 0)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
}

; 직접 그리는 창(확인 창·설정 창): 게임 화면처럼 보이게 창 틀 없이 전부 직접 그린다 (검은 격자 바탕,
; 노란 사선 띠 + 굵은 기울임 제목, 원형 닫기, 알약 버튼, 마우스를 올리면 노란 테). 빈 곳을 끌면 창이 움직이고, ESC·우클릭으로 닫는다
WV := {gui: 0, thumbs: [], rects: [], hot: "", down: "", status: "", track: false, paint: WipeViewPaint, act: WipeViewAct, close: WipeViewClose}
SV := {gui: 0, rects: [], hot: "", down: "", track: false, paint: SetViewPaint, act: SetViewAct, close: SetViewClose}
for msg in [0x200, 0x2A3, 0x201, 0x202, 0x205, 0x100]  ; 마우스 이동·벗어남·왼쪽 누름·뗌·오른쪽 뗌, 키 누름
    OnMessage msg, UiMsg

; 창을 만들어 (x, y)에 그려 띄운다. overlay면 게임 위에 포커스를 뺏지 않는 최상위 창으로 띄운다
; (게임이 계속 앞에 있어서 작업표시줄이 안 나오고 작업표시줄 단추도 안 생긴다. 대신 키 입력은 게임으로 가서 ESC는 안 받는다)
UiOpen(u, title, x, y, overlay := false) {
    u.gui := Gui(overlay ? "+AlwaysOnTop +ToolWindow -Caption -DPIScale +E0x08080000" : "-Caption -DPIScale +E0x80000", title)  ; LAYERED(+NOACTIVATE)
    u.gui.Show("NA Hide w1 h1")
    if overlay  ; 녹화에 보이기를 끄면 화면 캡처에서 뺀다. 켜도 판별과 안 겹치게 창 아래 끝을 "완료" 글자(0.936h) 위로 둔다
        UiCapture(u.gui.Hwnd)
    u.hot := "", u.down := "", u.track := false, u.overlay := overlay
    paint := u.paint, paint(x, y)
    if overlay
        return DllCall("ShowWindow", "ptr", u.gui.Hwnd, "int", 4)  ; SW_SHOWNOACTIVATE
    DllCall("ShowWindow", "ptr", u.gui.Hwnd, "int", 5)
    WinActivate u.gui.Hwnd
}

UiMsg(wp, lp, msg, hwnd) {
    for u in [PV, WV, SV]
        if u.gui && hwnd = u.gui.Hwnd
            return UiHandle(u, wp, lp, msg, hwnd)
}

UiHandle(u, wp, lp, msg, hwnd) {
    close := u.close
    if msg = 0x100 {  ; ESC로 닫기, 그 밖의 키는 창마다 (사진 보기: ←·→)
        if wp = 27
            close()
        else if u.HasOwnProp("key")
            key := u.key, key(wp)
        return 0
    }
    if msg = 0x205  ; 우클릭으로 닫기 (게임 메뉴의 우클릭 = ESC와 같게)
        return (close(), 0)
    if msg = 0x2A3  ; 마우스가 창을 벗어남
        return (u.track := false, UiHot(u, ""), 0)
    x := ((lp & 0xFFFF) ^ 0x8000) - 0x8000, y := (((lp >> 16) & 0xFFFF) ^ 0x8000) - 0x8000, act := ""
    for it in u.rects
        if x >= it.r.x && x < it.r.x + it.r.w && y >= it.r.y && y < it.r.y + it.r.h
            act := it.act
    if msg = 0x200 {
        if !u.track {  ; 창을 벗어날 때 알림(WM_MOUSELEAVE)을 받게 한다
            tme := Buffer(A_PtrSize = 8 ? 24 : 16, 0)
            NumPut("uint", tme.Size, "uint", 2, tme), NumPut("ptr", hwnd, tme, 8)
            u.track := DllCall("TrackMouseEvent", "ptr", tme)
        }
        UiHot(u, act)
    } else if msg = 0x201 {
        u.down := act
        if act = ""  ; 빈 곳을 끌면 창 이동
            PostMessage 0xA1, 2, 0, , "ahk_id " hwnd  ; WM_NCLBUTTONDOWN, HTCAPTION
    } else if act != "" && act = u.down
        SetTimer u.act.Bind(act), -1
    return 0
}

UiHot(u, act) {
    if act = u.hot
        return
    u.hot := act
    paint := u.paint, paint()
}

; 그리기 시작: W x H 투명 그림과 그 위에 그릴 GDI+ 판을 만든다
UiBegin(W, H) {
    c := {W: W, H: H}
    bi := Buffer(40, 0), NumPut("uint", 40, "int", W, "int", -H, "ushort", 1, "ushort", 32, bi)
    c.sdc := DllCall("GetDC", "ptr", 0, "ptr")
    c.mdc := DllCall("CreateCompatibleDC", "ptr", c.sdc, "ptr")
    c.dib := DllCall("CreateDIBSection", "ptr", c.mdc, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    c.old := DllCall("SelectObject", "ptr", c.mdc, "ptr", c.dib, "ptr")
    DllCall("gdiplus\GdipCreateBitmapFromScan0", "int", W, "int", H, "int", W * 4, "int", 0xE200B, "ptr", bits, "ptr*", &gb := 0)
    DllCall("gdiplus\GdipGetImageGraphicsContext", "ptr", gb, "ptr*", &g := 0)
    DllCall("gdiplus\GdipSetSmoothingMode", "ptr", g, "int", 4)
    DllCall("gdiplus\GdipSetTextRenderingHint", "ptr", g, "int", 4)
    c.gb := gb, c.g := g
    return c
}

; 그린 그림을 창에 올린다. x, y를 주면 그 자리로 옮기고, 안 주면 지금 자리 그대로
UiEnd(c, hwnd, x := "", y := "") {
    DllCall("gdiplus\GdipDeleteGraphics", "ptr", c.g)
    DllCall("gdiplus\GdipDisposeImage", "ptr", c.gb)
    pt := Buffer(8), NumPut("int", x = "" ? 0 : x, "int", y = "" ? 0 : y, pt)
    sz := Buffer(8), NumPut("int", c.W, "int", c.H, sz)
    blend := Buffer(4), NumPut("uchar", 0, "uchar", 0, "uchar", 255, "uchar", 1, blend)  ; AC_SRC_OVER, 255, AC_SRC_ALPHA
    DllCall("UpdateLayeredWindow", "ptr", hwnd, "ptr", c.sdc, "ptr", x = "" ? 0 : pt, "ptr", sz, "ptr", c.mdc, "ptr", Buffer(8, 0)
        , "uint", 0, "ptr", blend, "uint", 2)  ; ULW_ALPHA
    DllCall("SelectObject", "ptr", c.mdc, "ptr", c.old)
    DllCall("DeleteObject", "ptr", c.dib)
    DllCall("DeleteDC", "ptr", c.mdc)
    DllCall("ReleaseDC", "ptr", 0, "ptr", c.sdc)
}

; 창 틀: 둥근 검은 판 + 대각선 격자 + 회색 테, 노란 사선 띠 + 굵은 기울임 제목, 오른쪽 회색 영문 부제,
; 제목 아래 가는 선, 원형 닫기(검은 원 + 회색 테 + 흰 X. 마우스를 올리면 테가 노랑)
UiFrame(g, W, H, s, pad, title, sub, c, hot) {
    bg := WipeRoundRect(s, s, W - 2 * s, H - 2 * s, 16 * s)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", 0xF6080808, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", bg)
    DllCall("gdiplus\GdipCreateHatchBrush", "int", 5, "uint", 0xFF141414, "uint", 0x00000000, "ptr*", &hatch := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", hatch, "ptr", bg)
    DllCall("gdiplus\GdipCreatePen1", "uint", 0xFF3A3A3A, "float", 2 * s, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", bg)
    DllCall("gdiplus\GdipDeletePath", "ptr", bg)
    ty := 24 * s, tH := 38 * s
    UiTitle(g, title, pad - 2 * s, ty - 8 * s, c.x - 200 * s - pad, tH + 14 * s, 42 * s, s)
    WipeText(g, sub, pad, ty, c.x - 18 * s - pad, tH, 15 * s, 0xFF8A8A8A, 2, 0.2, "Noto Sans KR Medium", 0)
    DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", s)
    DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", 0xFF262626)
    DllCall("gdiplus\GdipDrawLine", "ptr", g, "ptr", pen, "float", pad, "float", 70 * s, "float", W - pad, "float", 70 * s)
    cx := c.x + c.w / 2, cy := c.y + c.h / 2
    DllCall("gdiplus\GdipSetSolidFillColor", "ptr", br, "uint", 0xFF050505)
    DllCall("gdiplus\GdipFillEllipse", "ptr", g, "ptr", br, "float", c.x, "float", c.y, "float", c.w, "float", c.h)
    DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", 2 * s)
    DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", hot = "close" ? 0xFFFFC83C : 0xFF4A4A4A)
    DllCall("gdiplus\GdipDrawEllipse", "ptr", g, "ptr", pen, "float", c.x, "float", c.y, "float", c.w, "float", c.h)
    DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", 0xFFFFFFFF)
    DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", 2.5 * s)
    k := 7 * s
    DllCall("gdiplus\GdipDrawLine", "ptr", g, "ptr", pen, "float", cx - k, "float", cy - k, "float", cx + k, "float", cy + k)
    DllCall("gdiplus\GdipDrawLine", "ptr", g, "ptr", pen, "float", cx + k, "float", cy - k, "float", cx - k, "float", cy + k)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", hatch)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
}

; 창 제목: 게임 메뉴 제목(적 이름 등)처럼 기울이지 않은 아주 굵은 글자에 위는 흰색·아래는 밝은 회색으로 내려가는 그라데이션,
; 오른쪽 아래로 진한 그림자와 얇은 어두운 테두리. 글자 모양을 경로로 만들어 그림자·채우기·테두리를 차례로 그린다
UiTitle(g, text, x, y, w, h, px, s) {
    DllCall("gdiplus\GdipCreateFontFamilyFromName", "wstr", "Noto Sans KR Black", "ptr", 0, "ptr*", &fam := 0)
    if !fam
        DllCall("gdiplus\GdipCreateFontFamilyFromName", "wstr", "Malgun Gothic", "ptr", 0, "ptr*", &fam := 0)
    DllCall("gdiplus\GdipCreateStringFormat", "int", 0x1000, "int", 0, "ptr*", &fmt := 0)
    DllCall("gdiplus\GdipSetStringFormatLineAlign", "ptr", fmt, "int", 1)
    DllCall("gdiplus\GdipCreatePath", "int", 1, "ptr*", &path := 0)  ; Winding: 굵은 한글 획이 겹친 곳이 비지 않게
    rc := Buffer(16), NumPut("float", x, "float", y, "float", w, "float", h, rc)
    DllCall("gdiplus\GdipAddPathString", "ptr", path, "wstr", text, "int", -1, "ptr", fam, "int", 1, "float", px, "ptr", rc, "ptr", fmt)
    ; 테두리: 글자 모양을 넓힌 경로를 어둡게 깐다 (선으로 그으면 굵은 한글 획이 겹친 안쪽 윤곽까지 그어짐).
    ; 그림자: 넓힌 경로를 오른쪽 아래로 옮겨 반투명 검정으로 먼저 깐다
    DllCall("gdiplus\GdipClonePath", "ptr", path, "ptr*", &op := 0)
    DllCall("gdiplus\GdipCreatePen1", "uint", 0xFF000000, "float", 2.4 * s, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipSetPenLineJoin", "ptr", pen, "int", 2)  ; 둥근 이음
    DllCall("gdiplus\GdipWidenPath", "ptr", op, "ptr", pen, "ptr", 0, "float", 0.25)
    DllCall("gdiplus\GdipSetPathFillMode", "ptr", op, "int", 1)
    DllCall("gdiplus\GdipClonePath", "ptr", op, "ptr*", &sp := 0)
    DllCall("gdiplus\GdipCreateMatrix2", "float", 1, "float", 0, "float", 0, "float", 1, "float", 2.5 * s, "float", 3.5 * s, "ptr*", &m := 0)
    DllCall("gdiplus\GdipTransformPath", "ptr", sp, "ptr", m)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", 0xB0000000, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", sp)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", path)  ; 넓힌 경로의 안쪽 구멍까지 메운다
    DllCall("gdiplus\GdipSetSolidFillColor", "ptr", br, "uint", 0xFF141414)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", op)
    ; 채우기: 글자 위쪽 흰색 → 아래쪽 밝은 회색
    p := Buffer(16), NumPut("float", 0, "float", y + h * 0.2, "float", 0, "float", y + h * 0.85, p)
    DllCall("gdiplus\GdipCreateLineBrush", "ptr", p, "ptr", p.Ptr + 8, "uint", 0xFFFFFFFF, "uint", 0xFFA8A8A8, "int", 3, "ptr*", &lb := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", lb, "ptr", path)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    DllCall("gdiplus\GdipDeletePath", "ptr", op)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", lb)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeleteMatrix", "ptr", m)
    DllCall("gdiplus\GdipDeletePath", "ptr", sp)
    DllCall("gdiplus\GdipDeletePath", "ptr", path)
    DllCall("gdiplus\GdipDeleteStringFormat", "ptr", fmt)
    DllCall("gdiplus\GdipDeleteFontFamily", "ptr", fam)
}

; 설정 한 줄 바탕: 조금 밝은 검은 둥근 판 + 어두운 회색 테 (마우스를 올리면 노랑)
UiPanel(g, x, y, w, h, s, hot) {
    p := WipeRoundRect(x, y, w, h, 10 * s)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", 0xFF0D0D0D, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", p)
    DllCall("gdiplus\GdipCreatePen1", "uint", hot ? 0xFFFFC83C : 0xFF262626, "float", (hot ? 2 : 1.5) * s, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", p)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeletePath", "ptr", p)
}

; 켜기/끄기 스위치: 켜면 노란 바탕에 검은 손잡이가 오른쪽, 끄면 검은 바탕에 회색 손잡이가 왼쪽
UiToggle(g, x, y, w, h, on, hot) {
    p := WipeRoundRect(x, y, w, h, h / 2)
    DllCall("gdiplus\GdipCreateSolidFill", "uint", on ? 0xFFFFC83C : 0xFF151515, "ptr*", &br := 0)
    DllCall("gdiplus\GdipFillPath", "ptr", g, "ptr", br, "ptr", p)
    DllCall("gdiplus\GdipCreatePen1", "uint", on || hot ? 0xFFFFC83C : 0xFF4A4A4A, "float", h * 0.07, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", p)
    m := h * 0.18, d := h - 2 * m
    DllCall("gdiplus\GdipSetSolidFillColor", "ptr", br, "uint", on ? 0xFF000000 : 0xFF8A8A8A)
    DllCall("gdiplus\GdipFillEllipse", "ptr", g, "ptr", br, "float", on ? x + w - m - d : x + m, "float", y + m, "float", d, "float", d)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeletePath", "ptr", p)
}

; 확인 창: 장면마다 미리보기(누르면 크게 열기)와 저장 버튼, 아래에 전체 저장·폴더 열기
ShowWipe(*) {
    if !WIPE.files.Length
        return TrayTip("아직 찍은 와이프아웃 장면이 없습니다", "젠존제 와이프아웃 캡처")
    WipeViewClose()
    ; 장면마다 한 줄 (왼쪽부터 깨끗한 순서). 찍은 장수와 상관없이 창 크기가 같도록 늘 4줄 x 장면당 캡처 수(설정) 칸으로 놓고
    ; 빈 칸은 어둡게 둔다. 화면 너비·높이의 90% 안에 들어가게 미리보기 크기를 정한다 (장면이 4개를 넘으면 앞의 4개만 보인다)
    s := A_ScreenDPI / 96, rows := 4, cols := CFG.per
    for row in WIPE.groups  ; 설정을 줄이기 전에 찍은 것도 다 보이게
        cols := Max(cols, row.Length)
    pad := Round(28 * s), gap := Round(18 * s), titleH := Round(84 * s), bh := Round(40 * s), cd := Round(38 * s), bg := Round(10 * s)
    thMax := (A_ScreenHeight * 0.85 - titleH - bh - pad) / rows - bg - bh - gap  ; 가운데에 놓으면 아래 끝 0.925h ("완료" 글자 위)
    tw := Round(Min(440 * s, (A_ScreenWidth * 0.9 - 2 * pad - (cols - 1) * gap) / cols, thMax / WIPE.ratio)), th := Round(tw * WIPE.ratio)
    W := 2 * pad + cols * tw + (cols - 1) * gap, botY := titleH + rows * (th + bg + bh + gap)
    W := Max(W, 2 * pad + Round(640 * s))  ; 장면당 1~2장이어도 아래 줄(자동 저장·전체 저장·폴더 열기)과 제목이 들어가게
    H := botY + bh + pad
    L := {s: s, pad: pad, tw: tw, th: th, bh: bh, bg: bg, W: W, H: H, botY: botY, thumbs: [], empty: []
        , close: {x: W - pad - cd, y: Round(24 * s), w: cd, h: cd}, bw: Round(190 * s)}
    rects := [{act: "close", r: L.close}]
    loop rows {
        r := A_Index, row := r <= WIPE.groups.Length ? WIPE.groups[r] : []
        loop cols {
            c := A_Index, x := pad + (c - 1) * (tw + gap), y := titleH + (r - 1) * (th + bg + bh + gap)
            if c > row.Length {
                L.empty.Push({x: x, y: y})
                continue
            }
            k := row[c]  ; WIPE.files 번호
            L.thumbs.Push({x: x, y: y, k: k, label: r "-" c})
            rects.Push({act: "open" k, r: {x: x, y: y, w: tw, h: th}}, {act: "save" k, r: {x: x, y: y + th + bg, w: tw, h: bh}})
        }
    }
    for t in L.thumbs {
        f := WIPE.files[t.k]
        ; 미리보기는 처음 한 번만 줄여 둔다 (마우스를 올릴 때마다 다시 그려서)
        DllCall("gdiplus\GdipCreateBitmapFromFile", "wstr", f, "ptr*", &img := 0)
        DllCall("gdiplus\GdipCreateBitmapFromScan0", "int", tw, "int", th, "int", 0, "int", 0xE200B, "ptr", 0, "ptr*", &tb := 0)
        DllCall("gdiplus\GdipGetImageGraphicsContext", "ptr", tb, "ptr*", &g := 0)
        DllCall("gdiplus\GdipSetInterpolationMode", "ptr", g, "int", 7)  ; 고품질 쌍삼차
        DllCall("gdiplus\GdipDrawImageRectI", "ptr", g, "ptr", img, "int", 0, "int", 0, "int", tw, "int", th)
        DllCall("gdiplus\GdipDeleteGraphics", "ptr", g)
        DllCall("gdiplus\GdipDisposeImage", "ptr", img)
        WV.thumbs.Push(tb)
    }
    ; 아래 줄은 오른쪽 끝부터: 저장 폴더 열기, 전체 저장, 자동 저장 스위치(왼쪽에 이름)
    L.folder := {x: W - pad - L.bw, y: botY, w: L.bw, h: bh}, L.all := {x: L.folder.x - gap - L.bw, y: botY, w: L.bw, h: bh}
    sw := Round(62 * s), sh := Round(32 * s)
    L.sw := {x: L.all.x - Round(28 * s) - sw, y: botY + (bh - sh) // 2, w: sw, h: sh}
    L.auto := {x: L.sw.x - Round(130 * s), y: botY, w: Round(130 * s) + sw, h: bh}  ; 이름과 스위치를 함께 누를 수 있게
    rects.Push({act: "auto", r: L.auto}, {act: "all", r: L.all}, {act: "folder", r: L.folder})
    WV.L := L, WV.rects := rects, WV.status := WIPE.note != "" ? WIPE.note : "미리보기를 누르면 크게 열립니다"
    ; 게임 화면 가운데에 띄운다
    try WinGetClientPos(&gx, &gy, &gw, &gh, GAME)
    catch
        gx := 0, gy := 0, gw := A_ScreenWidth, gh := A_ScreenHeight
    UiOpen(WV, "와이프아웃 캡처", gx + (gw - W) // 2, gy + (gh - H) // 2, !!WinActive(GAME))  ; 게임 중이면 게임 위에 바로
}

WipeViewPaint(x := "", y := "") {
    if !WV.gui
        return
    L := WV.L, s := L.s, hot := WV.hot
    c := UiBegin(L.W, L.H), g := c.g
    UiFrame(g, L.W, L.H, s, L.pad, "WIPEOUT", WIPE.groups.Length " SCENES  ·  " WIPE.files.Length " SHOTS", L.close, hot)
    ; 장면: 둥근 미리보기 + 테(마우스를 올리면 노랑) + 왼쪽 위 번호 띠, 아래에 저장 버튼
    DllCall("gdiplus\GdipCreatePen1", "uint", 0xFF3A3A3A, "float", 2 * s, "int", 2, "ptr*", &pen := 0)
    for e in L.empty  ; 빈 칸: 미리보기·버튼 자리를 어두운 판으로
        UiPanel(g, e.x, e.y, L.tw, L.th + L.bg + L.bh, s, false)
    for i, tb in WV.thumbs {
        t := L.thumbs[i], on := hot = "open" t.k
        rp := WipeRoundRect(t.x, t.y, L.tw, L.th, 10 * s)
        DllCall("gdiplus\GdipSetClipPath", "ptr", g, "ptr", rp, "int", 0)
        DllCall("gdiplus\GdipDrawImageRectI", "ptr", g, "ptr", tb, "int", t.x, "int", t.y, "int", L.tw, "int", L.th)
        DllCall("gdiplus\GdipResetClip", "ptr", g)
        DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", (on ? 3 : 2) * s)
        DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", on ? 0xFFFFC83C : 0xFF3A3A3A)
        DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", rp)
        DllCall("gdiplus\GdipDeletePath", "ptr", rp)
        WipeSlant(g, t.x + 12 * s, t.y + 12 * s, 46 * s, 26 * s, 6 * s, 0xE0000000)
        WipeText(g, t.label, t.x + 12 * s, t.y + 12 * s, 52 * s, 26 * s, 16 * s, on ? 0xFFFFC83C : 0xFFFFFFFF)
        WipeBtnPaint(g, L.tw, L.bh, L.bh / 0.0533, "저장", , t.x, t.y + L.th + L.bg, hot = "save" t.k)
    }
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    ; 아래 오른쪽: 자동 저장 스위치, 전체 저장, 저장 폴더 열기
    WipeText(g, "자동 저장", L.auto.x, L.botY, L.sw.x - L.auto.x - 12 * s, L.bh, 17 * s, hot = "auto" ? 0xFFFFC83C : 0xFFFFFFFF, 2)
    UiToggle(g, L.sw.x, L.sw.y, L.sw.w, L.sw.h, CFG.auto, hot = "auto")
    WipeBtnPaint(g, L.all.w, L.bh, L.bh / 0.0533, "전체 저장", , L.all.x, L.botY, hot = "all")
    WipeBtnPaint(g, L.folder.w, L.bh, L.bh / 0.0533, "저장 폴더 열기", , L.folder.x, L.botY, hot = "folder")
    UiEnd(c, WV.gui.Hwnd, x, y)
}

WipeViewAct(act) {
    if act = "close"
        WipeViewClose()
    else if act = "auto" {  ; 설정 창의 자동 저장과 같은 값
        CFG.auto := !CFG.auto, CfgSave(), WipeViewPaint()
        if SV.gui
            SetViewPaint()
    } else if act = "all"
        WipeSave(0)
    else if act = "folder"
        WipeOpenDir()
    else if SubStr(act, 1, 4) = "open"
        ShowPhoto(Integer(SubStr(act, 5)))
    else
        WipeSave(Integer(SubStr(act, 5)))
}

WipeStatus(text) {
    WV.status := text
    WipeViewPaint()
}

WipeViewClose() {
    PhotoClose()
    if WV.gui
        WV.gui.Destroy(), WV.gui := 0
    for tb in WV.thumbs
        DllCall("gdiplus\GdipDisposeImage", "ptr", tb)
    WV.thumbs := [], WV.hot := "", WV.track := false
}

; 사진 크게 보기: 확인 창 위에 게임 화면의 94% 크기로 띄운다. 좌우 원형 버튼(창이 포커스를 받으면 ←·→ 키도)으로
; 이전/다음 사진(끝에서는 처음으로), 아래 저장 버튼. X·우클릭·ESC로 닫으면 확인 창으로 돌아간다
PV := {gui: 0, rects: [], hot: "", down: "", track: false, paint: PhotoPaint, act: PhotoAct, close: PhotoClose, key: PhotoKey, img: 0, k: 0, msg: ""}

ShowPhoto(k) {
    overlay := WV.gui ? WV.overlay : !!WinActive(GAME)
    PhotoClose()
    try WinGetClientPos(&gx, &gy, &gw, &gh, GAME)
    catch
        gw := 0
    if gw < 800  ; 게임 창이 없거나 작으면 화면 기준
        gx := 0, gy := 0, gw := A_ScreenWidth, gh := A_ScreenHeight
    s := A_ScreenDPI / 96, pad := Round(28 * s), titleH := Round(84 * s), bh := Round(44 * s), cd := Round(38 * s)
    ad := Round(64 * s), side := pad + ad + Round(18 * s), gp := Round(16 * s)
    iw := gw * 0.94 - 2 * side, ih := iw * WIPE.ratio, maxIh := gh * 0.85 - titleH - gp - bh - pad  ; 아래 끝을 "완료" 글자 위로
    if ih > maxIh
        ih := maxIh, iw := ih / WIPE.ratio
    iw := Round(iw), ih := Round(ih), W := iw + 2 * side, H := titleH + ih + gp + bh + pad
    L := {s: s, pad: pad, W: W, H: H, ix: side, iy: titleH, iw: iw, ih: ih, bh: bh
        , close: {x: W - pad - cd, y: Round(24 * s), w: cd, h: cd}
        , prev: {x: pad, y: titleH + (ih - ad) // 2, w: ad, h: ad}, next: {x: W - pad - ad, y: titleH + (ih - ad) // 2, w: ad, h: ad}
        , save: {x: W - side - Round(190 * s), y: titleH + ih + gp, w: Round(190 * s), h: bh}}
    PV.L := L, PV.k := k, PV.msg := ""
    PV.rects := [{act: "close", r: L.close}, {act: "prev", r: L.prev}, {act: "next", r: L.next}, {act: "save", r: L.save}]
    PhotoLoad()
    UiOpen(PV, "와이프아웃 사진", gx + (gw - W) // 2, gy + (gh - H) // 2, overlay)
}

; 지금 사진을 보이는 크기로 한 번만 줄여 둔다 (마우스를 올릴 때마다 다시 그려서)
PhotoLoad() {
    if PV.img
        DllCall("gdiplus\GdipDisposeImage", "ptr", PV.img), PV.img := 0
    DllCall("gdiplus\GdipCreateBitmapFromFile", "wstr", WIPE.files[PV.k], "ptr*", &img := 0)
    DllCall("gdiplus\GdipCreateBitmapFromScan0", "int", PV.L.iw, "int", PV.L.ih, "int", 0, "int", 0xE200B, "ptr", 0, "ptr*", &tb := 0)
    DllCall("gdiplus\GdipGetImageGraphicsContext", "ptr", tb, "ptr*", &g := 0)
    DllCall("gdiplus\GdipSetInterpolationMode", "ptr", g, "int", 7)  ; 고품질 쌍삼차
    DllCall("gdiplus\GdipDrawImageRectI", "ptr", g, "ptr", img, "int", 0, "int", 0, "int", PV.L.iw, "int", PV.L.ih)
    DllCall("gdiplus\GdipDeleteGraphics", "ptr", g)
    DllCall("gdiplus\GdipDisposeImage", "ptr", img)
    PV.img := tb
}

PhotoPaint(x := "", y := "") {
    if !PV.gui
        return
    L := PV.L, s := L.s, hot := PV.hot, label := PV.k  ; 장면-순번 이름 (확인 창의 번호 띠와 같게)
    for r, row in WIPE.groups
        for c, k in row
            if k = PV.k
                label := r "-" c
    c := UiBegin(L.W, L.H), g := c.g
    UiFrame(g, L.W, L.H, s, L.pad, "장면 " label, PV.k " / " WIPE.files.Length, L.close, hot)
    rp := WipeRoundRect(L.ix, L.iy, L.iw, L.ih, 12 * s)
    DllCall("gdiplus\GdipSetClipPath", "ptr", g, "ptr", rp, "int", 0)
    DllCall("gdiplus\GdipDrawImageRectI", "ptr", g, "ptr", PV.img, "int", L.ix, "int", L.iy, "int", L.iw, "int", L.ih)
    DllCall("gdiplus\GdipResetClip", "ptr", g)
    DllCall("gdiplus\GdipCreatePen1", "uint", 0xFF3A3A3A, "float", 2 * s, "int", 2, "ptr*", &pen := 0)
    DllCall("gdiplus\GdipDrawPath", "ptr", g, "ptr", pen, "ptr", rp)
    DllCall("gdiplus\GdipDeletePath", "ptr", rp)
    ; 이전·다음: 닫기와 같은 검은 원 + 회색 테(마우스를 올리면 노랑) + 흰 꺾쇠
    DllCall("gdiplus\GdipCreateSolidFill", "uint", 0xFF050505, "ptr*", &br := 0)
    DllCall("gdiplus\GdipSetPenLineJoin", "ptr", pen, "int", 2)  ; 둥근 이음
    for d in [-1, 1] {
        b := d < 0 ? L.prev : L.next, cx := b.x + b.w / 2, cy := b.y + b.h / 2, k := 10 * s
        DllCall("gdiplus\GdipFillEllipse", "ptr", g, "ptr", br, "float", b.x, "float", b.y, "float", b.w, "float", b.h)
        DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", 2 * s)
        DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", hot = (d < 0 ? "prev" : "next") ? 0xFFFFC83C : 0xFF4A4A4A)
        DllCall("gdiplus\GdipDrawEllipse", "ptr", g, "ptr", pen, "float", b.x, "float", b.y, "float", b.w, "float", b.h)
        DllCall("gdiplus\GdipSetPenWidth", "ptr", pen, "float", 3.5 * s)
        DllCall("gdiplus\GdipSetPenColor", "ptr", pen, "uint", 0xFFFFFFFF)
        pts := Buffer(24), NumPut("float", cx - d * k / 2, "float", cy - k, "float", cx + d * k / 2, "float", cy, "float", cx - d * k / 2, "float", cy + k, pts)
        DllCall("gdiplus\GdipDrawLines", "ptr", g, "ptr", pen, "ptr", pts, "int", 3)
    }
    DllCall("gdiplus\GdipDeleteBrush", "ptr", br)
    DllCall("gdiplus\GdipDeletePen", "ptr", pen)
    if PV.msg != ""
        WipeText(g, PV.msg, L.ix, L.save.y, L.save.x - L.ix - 24 * s, L.bh, 15 * s, 0xFF9A9A9A, 2, 0.2, "Noto Sans KR Medium", 0, 5)
    WipeBtnPaint(g, L.save.w, L.bh, L.bh / 0.0533, "이 사진 저장", , L.save.x, L.save.y, hot = "save")
    UiEnd(c, PV.gui.Hwnd, x, y)
}

PhotoAct(act) {
    if act = "close"
        return PhotoClose()
    if act = "save"
        PV.msg := WipeSave(PV.k)
    else
        PhotoGo(act = "next" ? 1 : -1)
    PhotoPaint()
}

PhotoKey(vk) {
    if vk = 37 || vk = 39  ; ← →
        PhotoGo(vk = 39 ? 1 : -1), PhotoPaint()
}

PhotoGo(d) {
    n := WIPE.files.Length
    PV.k := Mod(PV.k - 1 + d + n, n) + 1, PV.msg := ""
    PhotoLoad()
}

PhotoClose() {
    if PV.gui
        PV.gui.Destroy(), PV.gui := 0
    if PV.img
        DllCall("gdiplus\GdipDisposeImage", "ptr", PV.img), PV.img := 0
    PV.hot := "", PV.track := false
}

; 설정 창: 저장 폴더(변경·열기), 자동 저장, 결과 화면 버튼 표시. 바꾸면 바로 설정 파일에 저장한다
ShowSettings(*) {
    if SV.gui
        return WinActivate(SV.gui.Hwnd)
    s := A_ScreenDPI / 96, pad := Round(28 * s), W := Round(700 * s), titleH := Round(84 * s), cd := Round(38 * s)
    rh := Round(80 * s), rg := Round(12 * s), bh := Round(44 * s), bw := Round(96 * s), sbh := Round(40 * s)
    tw := Round(62 * s), th := Round(32 * s), rx := pad, rw := W - 2 * pad, y := titleH + Round(4 * s)
    L := {s: s, pad: pad, W: W, bh: bh, sb: {w: bw, h: sbh}, rows: [], close: {x: W - pad - cd, y: Round(24 * s), w: cd, h: cd}}
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "저장 폴더", act: "", descW: rw - Round(20 * s) - 2 * bw - Round(46 * s)})
    L.chg := {x: rx + rw - Round(18 * s) - 2 * bw - Round(10 * s), y: y + (rh - sbh) // 2, w: bw, h: sbh}
    L.opn := {x: rx + rw - Round(18 * s) - bw, y: y + (rh - sbh) // 2, w: bw, h: sbh}
    y += rh + rg
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "자동 저장", act: "auto", descW: rw - Round(120 * s)
        , desc: "캡처가 끝나면 모든 장면을 저장 폴더에 바로 저장합니다"})
    L.t1 := {x: rx + rw - Round(22 * s) - tw, y: y + (rh - th) // 2, w: tw, h: th}
    y += rh + rg
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "결과 화면 버튼", act: "btn", descW: rw - Round(120 * s)
        , desc: "도전 결과 화면의 '한 번 더' 옆에 와이프아웃 버튼을 띄웁니다"})
    L.t2 := {x: rx + rw - Round(22 * s) - tw, y: y + (rh - th) // 2, w: tw, h: th}
    y += rh + rg
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "원본 화질 (PNG)", act: "png", descW: rw - Round(120 * s)
        , desc: "켜면 무손실 PNG(장당 4~5MB), 끄면 고화질 JPG(장당 1MB 안팎)로 저장합니다"})
    L.t3 := {x: rx + rw - Round(22 * s) - tw, y: y + (rh - th) // 2, w: tw, h: th}
    y += rh + rg
    ; 장면당 캡처 수: 오른쪽에 [-] 숫자 [+]
    pw := Round(52 * s), nw := Round(56 * s)
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "장면당 캡처 수", act: "", descW: rw - Round(40 * s) - 2 * pw - nw
        , desc: "한 장면에서 깨끗한 순서로 남길 장수 (1~10장)"})
    L.minus := {x: rx + rw - Round(22 * s) - 2 * pw - nw, y: y + (rh - sbh) // 2, w: pw, h: sbh}
    L.num := {x: L.minus.x + pw, y: y, w: nw, h: rh}
    L.plus := {x: L.num.x + nw, y: L.minus.y, w: pw, h: sbh}
    y += rh + rg
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "녹화에 보이기", act: "rec", descW: rw - Round(120 * s)
        , desc: "켜면 결과 화면 버튼과 확인 창이 녹화·스크린샷에 찍힙니다"})
    L.t4 := {x: rx + rw - Round(22 * s) - tw, y: y + (rh - th) // 2, w: tw, h: th}
    y += rh + rg
    L.rows.Push({x: rx, y: y, w: rw, h: rh, label: "Windows 로그인 시 자동 실행", act: "boot", descW: rw - Round(120 * s)
        , desc: "켜면 Windows에 로그인할 때 이 프로그램이 트레이에서 켜집니다"})
    L.t5 := {x: rx + rw - Round(22 * s) - tw, y: y + (rh - th) // 2, w: tw, h: th}
    y += rh + rg
    L.botY := y + Round(6 * s), L.H := L.botY + bh + pad
    L.done := {x: W - pad - Round(160 * s), y: L.botY, w: Round(160 * s), h: bh}
    SV.L := L
    SV.rects := [{act: "close", r: L.close}, {act: "dir", r: L.chg}, {act: "opendir", r: L.opn}
        , {act: "auto", r: L.rows[2]}, {act: "btn", r: L.rows[3]}, {act: "png", r: L.rows[4]}
        , {act: "per-", r: L.minus}, {act: "per+", r: L.plus}, {act: "rec", r: L.rows[6]}, {act: "boot", r: L.rows[7]}, {act: "done", r: L.done}]
    UiOpen(SV, "젠존제 와이프아웃 설정", (A_ScreenWidth - W) // 2, (A_ScreenHeight - L.H) // 2)
}

SetViewPaint(x := "", y := "") {
    if !SV.gui
        return
    L := SV.L, s := L.s, hot := SV.hot
    c := UiBegin(L.W, L.H), g := c.g
    UiFrame(g, L.W, L.H, s, L.pad, "설정", "WIPEOUT CAPTURE", L.close, hot)
    L.rows[1].desc := WipeSaveDirs()[1] . (CFG.dir = "" ? "  (기본)" : "")
    for r in L.rows {
        UiPanel(g, r.x, r.y, r.w, r.h, s, r.act != "" && hot = r.act)
        WipeText(g, r.label, r.x + 20 * s, r.y + 10 * s, r.descW, 32 * s, 19 * s, 0xFFFFFFFF, 0)
        WipeText(g, r.desc, r.x + 22 * s, r.y + 42 * s, r.descW, 26 * s, 14 * s, 0xFF8A8A8A, 0, 0.2, "Noto Sans KR Medium", 0, 5)
    }
    WipeBtnPaint(g, L.chg.w, L.chg.h, L.chg.h / 0.0533, "변경", , L.chg.x, L.chg.y, hot = "dir")
    WipeBtnPaint(g, L.opn.w, L.opn.h, L.opn.h / 0.0533, "열기", , L.opn.x, L.opn.y, hot = "opendir")
    UiToggle(g, L.t1.x, L.t1.y, L.t1.w, L.t1.h, CFG.auto, hot = "auto")
    UiToggle(g, L.t2.x, L.t2.y, L.t2.w, L.t2.h, CFG.btn, hot = "btn")
    UiToggle(g, L.t3.x, L.t3.y, L.t3.w, L.t3.h, CFG.png, hot = "png")
    UiToggle(g, L.t4.x, L.t4.y, L.t4.w, L.t4.h, CFG.rec, hot = "rec")
    UiToggle(g, L.t5.x, L.t5.y, L.t5.w, L.t5.h, BootOn(), hot = "boot")
    WipeBtnPaint(g, L.minus.w, L.minus.h, L.minus.h / 0.0533, "−", , L.minus.x, L.minus.y, hot = "per-")
    WipeText(g, CFG.per, L.num.x, L.num.y, L.num.w, L.num.h, 22 * s, 0xFFFFC83C)
    WipeBtnPaint(g, L.plus.w, L.plus.h, L.plus.h / 0.0533, "+", , L.plus.x, L.plus.y, hot = "per+")
    WipeText(g, "바꾼 설정은 바로 저장됩니다", L.pad + 4 * s, L.botY, L.done.x - L.pad - 20 * s, L.bh, 14 * s, 0xFF6A6A6A, 0, 0.2, "Noto Sans KR Medium", 0)
    WipeBtnPaint(g, L.done.w, L.bh, L.bh / 0.0533, "닫기", , L.done.x, L.botY, hot = "done")
    UiEnd(c, SV.gui.Hwnd, x, y)
}

SetViewAct(act) {
    if act = "close" || act = "done"
        return SetViewClose()
    if act = "auto"
        CFG.auto := !CFG.auto
    else if act = "btn"
        CFG.btn := !CFG.btn
    else if act = "png"
        CFG.png := !CFG.png
    else if act = "per-" || act = "per+"
        CFG.per := Max(1, Min(10, CFG.per + (act = "per+" ? 1 : -1)))
    else if act = "rec" {  ; 지금 떠 있는 버튼·창에도 바로 적용
        CFG.rec := !CFG.rec
        for hw in [WIPE_BTN.Hwnd, WV.gui && WV.overlay ? WV.gui.Hwnd : 0, PV.gui && PV.overlay ? PV.gui.Hwnd : 0]
            if hw
                UiCapture(hw)
    }
    else if act = "boot"
        BootSet(!BootOn())
    else if act = "opendir"
        return WipeOpenDir()
    else if act = "dir" {
        d := DirSelect("*" WipeSaveDirs()[1], 3, "와이프아웃 장면을 저장할 폴더를 고르세요")
        if d = ""
            return
        CFG.dir := d
    }
    CfgSave()
    SetViewPaint()
}

SetViewClose() {
    if SV.gui
        SV.gui.Destroy(), SV.gui := 0
    SV.hot := "", SV.track := false
}

CfgSave() {
    try {
        DirCreate CFG_DIR
        IniWrite CFG.dir, CFG_INI, "설정", "저장폴더"
        IniWrite CFG.auto ? 1 : 0, CFG_INI, "설정", "자동저장"
        IniWrite CFG.btn ? 1 : 0, CFG_INI, "설정", "결과버튼"
        IniWrite CFG.png ? 1 : 0, CFG_INI, "설정", "PNG"
        IniWrite CFG.per, CFG_INI, "설정", "장면당캡처"
        IniWrite CFG.rec ? 1 : 0, CFG_INI, "설정", "녹화에보이기"
    }
}

; i번 장면만(0이면 전체) "찍은시각_번호.png"로 저장하고 결과 문구를 돌려준다. 사진 폴더는 Windows 보안의 제어된 폴더 액세스가
; AutoHotkey 쓰기를 막을 수 있어서, 막히면 사용자 폴더(C:\Users\이름\젠존제 와이프아웃)에 저장한다
WipeSave(i, *) {
    for dir in WipeSaveDirs() {
        n := 0
        try {
            DirCreate dir
            for f in WIPE.files
                if !i || A_Index = i
                    SplitPath(f, &fn), FileCopy(f, dir "\" WIPE.stamp "_" fn, 1), n++  ; 찍은시각_장면-순번.jpg
            WIPE.savedDir := dir
            WipeStatus(msg := n "장 저장: " dir)
            return msg
        } catch as e
            err := e.Message
    }
    WipeStatus(msg := "저장 실패: " err)
    return msg
}

; 마지막으로 저장한 폴더를, 아직 저장 전이면 저장 폴더를 연다 (없으면 만든다)
WipeOpenDir(*) {
    if WIPE.HasProp("savedDir") && DirExist(WIPE.savedDir)
        return Run(WIPE.savedDir)
    for dir in WipeSaveDirs()
        if DirExist(dir)
            return Run(dir)
    for dir in WipeSaveDirs()
        try {
            DirCreate dir
            return Run(dir)
        }
}

; 저장할 폴더 순서: 설정한 폴더(없으면 사진\젠존제 와이프아웃), 막히면 사용자 폴더\젠존제 와이프아웃
WipeSaveDirs() {
    static pics := ""
    if pics = "" {
        guid := Buffer(16), DllCall("ole32\CLSIDFromString", "wstr", "{33E28130-4E1E-4676-835A-98395C3BC3BB}", "ptr", guid)  ; 사진 폴더
        pics := (DllCall("shell32\SHGetKnownFolderPath", "ptr", guid, "uint", 0, "ptr", 0, "ptr*", &p := 0) ? A_MyDocuments : StrGet(p, "UTF-16")) "\젠존제 와이프아웃"
        DllCall("ole32\CoTaskMemFree", "ptr", p)
    }
    return [CFG.dir != "" ? CFG.dir : pics, EnvGet("USERPROFILE") "\젠존제 와이프아웃"]
}

SetTimer Watch, 100
if !FROM_LOGON  ; 직접 실행했으면 설정 창을 연다 (처음 실행이면 설정 파일도 만든다)
    (FIRST_RUN && CfgSave()), ShowSettings()

; 게임 창이 앞에 있을 때만 판별한다. 다른 창으로 가면 결과 화면 버튼을 숨긴다
Watch() {
    if !WinActive(GAME)
        return WIPE_BTN.Hide()
    try {
        if !WIPE.on && WipeTop(CursorShown())
            StartWipe()
        WipeButton()
    } catch as e
        try WipeLog("오류: " e.Message " (" e.What " " e.Line "줄)")
}

; 화면 일부를 캡처해 점의 밝기를 읽을 수 있는 버퍼로 돌려준다
GrabLum(x, y, cw, ch) {
    bi := Buffer(40, 0), NumPut("uint", 40, "int", cw, "int", -ch, "ushort", 1, "ushort", 32, bi)
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    mdc := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    dib := DllCall("CreateDIBSection", "ptr", mdc, "ptr", bi, "uint", 0, "ptr*", &bits := 0, "ptr", 0, "uint", 0, "ptr")
    old := DllCall("SelectObject", "ptr", mdc, "ptr", dib, "ptr")
    DllCall("BitBlt", "ptr", mdc, "int", 0, "int", 0, "int", cw, "int", ch, "ptr", sdc, "int", x, "int", y, "uint", 0x00CC0020)
    DllCall("GdiFlush")
    buf := Buffer(cw * ch * 4)
    DllCall("RtlMoveMemory", "ptr", buf, "ptr", bits, "uptr", buf.Size)
    DllCall("SelectObject", "ptr", mdc, "ptr", old)
    DllCall("DeleteObject", "ptr", dib)
    DllCall("DeleteDC", "ptr", mdc)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    return buf
}

ParsePts(s, white, out := []) {
    for xy in StrSplit(s, " ") {
        p := StrSplit(xy, ",")
        out.Push([Float(p[1]), Float(p[2]), white])
    }
    return out
}

ResultScreen() {
    try WinGetClientPos(, , &w, &h, GAME)
    catch
        return false
    return MatchText(DONE, w, h)
}

; 흰 지점·검은 지점이 각각 85% 이상 맞으면 일치
MatchText(b, w, h) {
    pts := b.pts, lum := Sample(pts, w, h), okw := okd := nw := 0
    for i, p in pts
        if p[3]
            nw++, okw += lum[i] > 170
        else
            okd += lum[i] < 70
    return okw >= nw * 0.85 && okd >= (pts.Length - nw) * 0.85
}

; 지점들을 감싸는 영역만 한 번 캡처해서 밝기를 읽음
Sample(points, w, h) {
    WinGetClientPos(&cx, &cy, , , GAME)
    xy := [], x0 := y0 := 99999, x1 := y1 := 0
    for p in points {
        x := Round(w * p[1]), y := Round(h * p[2])
        xy.Push([x, y]), x0 := Min(x0, x), y0 := Min(y0, y), x1 := Max(x1, x), y1 := Max(y1, y)
    }
    bw := x1 - x0 + 1, bh := y1 - y0 + 1
    sdc := DllCall("GetDC", "ptr", 0, "ptr")
    mdc := DllCall("CreateCompatibleDC", "ptr", sdc, "ptr")
    bmp := DllCall("CreateCompatibleBitmap", "ptr", sdc, "int", bw, "int", bh, "ptr")
    old := DllCall("SelectObject", "ptr", mdc, "ptr", bmp, "ptr")
    DllCall("BitBlt", "ptr", mdc, "int", 0, "int", 0, "int", bw, "int", bh
        , "ptr", sdc, "int", cx + x0, "int", cy + y0, "uint", 0x00CC0020)  ; SRCCOPY
    out := []
    for p in xy {
        c := DllCall("GetPixel", "ptr", mdc, "int", p[1] - x0, "int", p[2] - y0, "uint")
        out.Push(((c & 0xFF) + ((c >> 8) & 0xFF) + ((c >> 16) & 0xFF)) / 3)
    }
    DllCall("SelectObject", "ptr", mdc, "ptr", old)
    DllCall("DeleteObject", "ptr", bmp)
    DllCall("DeleteDC", "ptr", mdc)
    DllCall("ReleaseDC", "ptr", 0, "ptr", sdc)
    return out
}

CursorShown() {
    ci := Buffer(A_PtrSize = 8 ? 24 : 20, 0)
    NumPut("uint", ci.Size, ci)
    if !DllCall("GetCursorInfo", "ptr", ci)
        return false
    return NumGet(ci, 4, "uint") & 1  ; CURSOR_SHOWING
}
