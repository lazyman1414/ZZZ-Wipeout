using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ZzzWipeout
{
    // 설정: 저장 폴더(비우면 사진\젠존제 와이프아웃), 자동 저장, 결과 화면 버튼 표시 등.
    // 문서·사진 폴더는 Windows 보안이 막을 수 있어서 설정 파일은 AppData에 둔다 (AutoHotkey판과 같은 파일)
    static class Cfg
    {
        public static readonly string Dir0 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "젠존제 와이프아웃");
        public static readonly string Ini = Path.Combine(Dir0, "설정.ini");

        public static string Dir = "";
        public static bool Auto, Btn = true, Debug, Png, Rec = true;
        public static int Per = 3;
        public static bool FirstRun;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string sec, string key, string def, StringBuilder ret, int size, string file);
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string sec, string key, string val, string file);

        static string Read(string key, string def)
        {
            var sb = new StringBuilder(2048);
            GetPrivateProfileString("설정", key, def, sb, sb.Capacity, Ini);
            return sb.ToString().Trim();
        }

        static bool ReadBool(string key, int def) => int.TryParse(Read(key, def.ToString()), out int v) ? v == 1 : def == 1;

        public static void Load()
        {
            FirstRun = !File.Exists(Ini);
            Dir = Read("저장폴더", "");
            Auto = ReadBool("자동저장", 0);
            Btn = ReadBool("결과버튼", 1);
            Debug = ReadBool("디버그", 0);
            Png = ReadBool("PNG", 0);
            Per = Math.Max(1, Math.Min(10, int.TryParse(Read("장면당캡처", "3"), out int p) ? p : 3));
            Rec = ReadBool("녹화에보이기", 1);
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir0);
                if (!File.Exists(Ini))  // AutoHotkey의 IniWrite처럼 UTF-16 파일로 시작해야 한글이 깨지지 않는다
                    File.WriteAllBytes(Ini, new byte[] { 0xFF, 0xFE });
                WritePrivateProfileString("설정", "저장폴더", Dir, Ini);
                WritePrivateProfileString("설정", "자동저장", Auto ? "1" : "0", Ini);
                WritePrivateProfileString("설정", "결과버튼", Btn ? "1" : "0", Ini);
                WritePrivateProfileString("설정", "PNG", Png ? "1" : "0", Ini);
                WritePrivateProfileString("설정", "장면당캡처", Per.ToString(), Ini);
                WritePrivateProfileString("설정", "녹화에보이기", Rec ? "1" : "0", Ini);
            }
            catch { }
        }

        // Windows 로그인 시 자동 실행(기본 꺼짐): 설정 창에서 켜면 현재 사용자 시작 프로그램(레지스트리 Run)에 등록한다. 관리자 권한은 필요 없다
        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run", RUN_NAME = "ZZZ-Wipeout";
        static string RunCmd => "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --logon";

        public static bool BootOn()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                    return !string.IsNullOrEmpty(k?.GetValue(RUN_NAME) as string);
            }
            catch { return false; }
        }

        // 자동 실행이 켜져 있는데 기록된 위치가 지금 exe와 다르면 (exe를 옮긴 경우) 지금 위치로 고친다
        public static void BootFix()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    string v = k?.GetValue(RUN_NAME) as string;
                    if (!string.IsNullOrEmpty(v) && !string.Equals(v, RunCmd, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(RUN_NAME, RunCmd);
                }
            }
            catch { }
        }

        public static void BootSet(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (on)
                        k.SetValue(RUN_NAME, RunCmd);
                    else
                        k.DeleteValue(RUN_NAME, false);
                }
            }
            catch { }
        }
    }
}
