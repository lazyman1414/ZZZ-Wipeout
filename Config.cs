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

        // Windows 로그인 시 자동 실행(기본 꺼짐): 설정 창에서 켜면 작업 스케줄러에 "로그인할 때, 가장 높은 권한으로" 등록한다.
        // 이 프로그램은 관리자 권한으로 돌아야 해서(app.manifest) 레지스트리 Run으로는 로그인 때 실행되지 않는다
        const string TASK = "ZZZ-Wipeout", RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static string Exe => System.Windows.Forms.Application.ExecutablePath;
        static bool? bootCache;  // 설정 창이 다시 그릴 때마다 schtasks를 부르지 않게

        static string Schtasks(string args, out int code)
        {
            code = -1;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    code = p.ExitCode;
                    return o;
                }
            }
            catch { return ""; }
        }

        public static bool BootOn()
        {
            if (bootCache == null)
            {
                Schtasks("/Query /TN \"" + TASK + "\"", out int code);
                bootCache = code == 0;
            }
            return bootCache.Value;
        }

        public static void BootSet(bool on)
        {
            bootCache = null;
            if (!on)
            {
                Schtasks("/Delete /TN \"" + TASK + "\" /F", out _);
                return;
            }
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string X(string s) => System.Security.SecurityElement.Escape(s);
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "<RegistrationInfo><Description>ZZZ 와이프아웃 캡처 자동 실행</Description></RegistrationInfo>\n"
                + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + X(user) + "</UserId></LogonTrigger></Triggers>\n"
                + "<Principals><Principal id=\"Author\"><UserId>" + X(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n"
                + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\n"
                + "<Actions Context=\"Author\"><Exec><Command>" + X(Exe) + "</Command><Arguments>--logon</Arguments></Exec></Actions>\n</Task>";
            string f = Path.Combine(Path.GetTempPath(), "ZZZ-Wipeout-task.xml");
            try
            {
                File.WriteAllText(f, xml, Encoding.Unicode);
                Schtasks("/Create /TN \"" + TASK + "\" /XML \"" + f + "\" /F", out _);
            }
            catch { }
            try { File.Delete(f); } catch { }
        }

        // 시작할 때: 예전 판이 레지스트리 Run에 등록한 자동 실행은 작업 스케줄러로 옮기고,
        // 자동 실행이 켜져 있는데 기록된 위치가 지금 exe와 다르면 (exe를 옮긴 경우) 지금 위치로 고친다
        public static void BootFix()
        {
            bool old = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                    if (k?.GetValue(TASK) != null)
                    {
                        k.DeleteValue(TASK, false);
                        old = true;
                    }
            }
            catch { }
            if (old || (BootOn() && Schtasks("/Query /TN \"" + TASK + "\" /XML", out _).IndexOf("<Command>" + System.Security.SecurityElement.Escape(Exe) + "</Command>", StringComparison.OrdinalIgnoreCase) < 0))
                BootSet(true);
        }
    }
}
