using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AppKiller
{
#region
    //https://stackoverflow.com/questions/1363167/how-can-i-get-the-child-windows-of-a-window-given-its-hwnd
    public class WindowHandleInfo
    {
        private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        private IntPtr _MainHandle;

        public WindowHandleInfo(IntPtr handle)
        {
            this._MainHandle = handle;
        }

        public List<IntPtr> GetAllChildHandles()
        {
            List<IntPtr> childHandles = new List<IntPtr>();

            GCHandle gcChildhandlesList = GCHandle.Alloc(childHandles);
            IntPtr pointerChildHandlesList = GCHandle.ToIntPtr(gcChildhandlesList);

            try
            {
                EnumWindowProc childProc = new EnumWindowProc(EnumWindow);
                EnumChildWindows(this._MainHandle, childProc, pointerChildHandlesList);
            }
            finally
            {
                gcChildhandlesList.Free();
            }

            return childHandles;
        }

        private bool EnumWindow(IntPtr hWnd, IntPtr lParam)
        {
            GCHandle gcChildhandlesList = GCHandle.FromIntPtr(lParam);

            if (gcChildhandlesList == null || gcChildhandlesList.Target == null)
            {
                return false;
            }

            List<IntPtr> childHandles = gcChildhandlesList.Target as List<IntPtr>;
            childHandles.Add(hWnd);

            return true;
        }

        public List<IntPtr> GetAllTopOwnedHandles(IntPtr ownerHWnd)
        {
            List<IntPtr> childHandles = new List<IntPtr>();
            List<IntPtr> ownedChildHandles = new List<IntPtr>();

            GCHandle gcChildhandlesList = GCHandle.Alloc(childHandles);
            IntPtr pointerChildHandlesList = GCHandle.ToIntPtr(gcChildhandlesList);

            try
            {
                EnumWindowProc childProc = new EnumWindowProc(EnumWindow);
                EnumChildWindows((IntPtr)0, childProc, pointerChildHandlesList); //get Desktop child window
            }
            finally
            {
                gcChildhandlesList.Free();
            }

            foreach (IntPtr intPtr in childHandles)
            {
                IntPtr owner = GetWindow(intPtr, 4); //GW_OWNER=4
                if (ownerHWnd == owner)
                {
                    ownedChildHandles.Add(intPtr);
                }
            }

            return ownedChildHandles;
        }

    }

    //EXAMPLE
    //class Program
    //{
    //    [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
    //    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    //    static void Main(string[] args)
    //    {
    //        Process[] anotherApps = Process.GetProcessesByName("AnotherApp");
    //        if (anotherApps.Length == 0) return;
    //        if (anotherApps[0] != null)
    //        {
    //            var allChildWindows = new WindowHandleInfo(anotherApps[0].MainWindowHandle).GetAllChildHandles();
    //        }
    //    }
    //}

    #endregion

    class Program
    {
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        static void Main(string[] args)
        {
            //проверим что программа не запущена дважды
            Process cProc = System.Diagnostics.Process.GetCurrentProcess();

            Process[] processList = Process.GetProcessesByName(cProc.ProcessName);
            if (processList.Length > 1)
            {
                return;
            }

            IntPtr h = Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(h, 0);

            //SendEmailAsync().GetAwaiter();


            //v1
            //int sleepSec = 10; //спим после итерации
            //v2
            int sleepSec = 30; //спим после итерации

            // v1
            //long closeWndTimeoutSec = 60; //таймаут реакции кользователя на диалог закрытия
            //v2
            //long closeWndTimeoutSec = 10 * 60; //таймаут реакции кользователя на диалог закрытия

            //v3
            long closeWndTimeoutSec = 5 * 60; //таймаут реакции кользователя на диалог закрытия

            TimeSpan exitTime = new TimeSpan(17, 30, 0); //время завершения программы

//            Regex titleRegEx = new Regex(@"^АБС Finist\.\sОператор:[^\[]*\[9983\]", RegexOptions.Compiled);
            Regex titleRegEx = new Regex(@"^АБС Finist\.\sОператор:[^\[]*\[\d{4}\]", RegexOptions.Compiled);
            string closeWndRegEx = @"^(Завершение)|(Введите пароль)$";

            //тест на себе
            //Regex domainUserRegEx = new Regex(@"^(ri.khalfin.*)", RegexOptions.Compiled);
            //v1 кассиры
            //Regex domainUserRegEx = new Regex(@"^(касс.*)|(kass.*)|(оквку.*)|(okvku.*)", RegexOptions.Compiled);

            //v2 все
            Regex domainUserRegEx = new Regex(@"^.*", RegexOptions.Compiled);

            string AccountName = System.Environment.UserName.ToLower();
            if (!domainUserRegEx.IsMatch(AccountName))
            {
                return;
            }

            long closeWndTimeout = closeWndTimeoutSec * 1000; //таймаут (в миллисекундах)
            Dictionary<int, ProcInfo> procInfoDict = new Dictionary<int, ProcInfo>();


            //Периодическая итерация
            do
            {
                if (DateTime.Now > DateTime.Now.Date.Add(exitTime)) //вечером программа завершается.
                {
                    return;
                }

                //получаем новый список процессов
                List<Process> procList = ProcessByWinTitle(titleRegEx);

                //дополняем procInfo
                foreach (Process proc in procList)
                {
                    if (!procInfoDict.ContainsKey(proc.Id))
                    {
                        ProcInfo procInfo = new ProcInfo(proc, closeWndRegEx, closeWndTimeout);
                        procInfoDict.Add(proc.Id, procInfo);
                    }

                }

                //удаляем procInfo для которых не обнаружены процессы.
                foreach (int procInfoPID in procInfoDict.Keys)
                {
                    if (!procList.Exists(e => e.Id.Equals(procInfoPID)))
                    {
                        procInfoDict.Remove(procInfoPID);
                        break; //выходим, так как после изменения коллекции продолжать итерации нельзя.
                    }
                }


                //для актуальных procInfo обновляем состояние.
                foreach (ProcInfo procInfo in procInfoDict.Values)
                {
                    procInfo.UpdateWinState();
                }

                System.Threading.Thread.Sleep(sleepSec * 1000); //спим перед повтором итерации

            }
            while (true);

        }

        private enum WinState { init = -1, FG = 0, BG, BG_CloseWnd }; //0 - окно foreground, 1 - окно background, 2 - окно background и всплыл запрос закрытия окна.

        private class ProcInfo {
            [DllImport("user32.dll")]
            static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
            static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            static extern int GetWindowText(IntPtr hWnd, StringBuilder title, int size);

            private Process procObj;
            private WinState? winState;
            private long stateTimestamp;

            private Regex closeWndRegEx;
            private long closeWndTimeout;

            public ProcInfo (Process procObj, string closeWndRegEx, long closeWndTimeout)
            {
                this.procObj = procObj;
                SetState(WinState.init);

                this.closeWndRegEx = new Regex(closeWndRegEx, RegexOptions.Compiled);
                this.closeWndTimeout = closeWndTimeout;
            }

            void SetState(WinState winState)
            {
                if (this.winState != winState) //если статус не изменялся то не обновляем
                {
                    this.winState = winState;
                    
                    //this.stateTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    this.stateTimestamp = DateTimeOffset.Now.Ticks;
                }
            }

            public void UpdateWinState()
            {
                IntPtr fgWindow = GetForegroundWindow();
                if (procObj.MainWindowHandle == fgWindow)
                {
                    SetState(WinState.FG);
                }
                else
                {
                    List<IntPtr> allChildWindows = new WindowHandleInfo(procObj.MainWindowHandle).GetAllChildHandles();
                    List<IntPtr> allModalChildWindows = new WindowHandleInfo(procObj.MainWindowHandle).GetAllTopOwnedHandles(procObj.MainWindowHandle);
                    allChildWindows.AddRange(allModalChildWindows);

                    IntPtr? closeWndHandle = null;

                    foreach (IntPtr hWnd in allChildWindows)
                    {
                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, 256);

                        if (closeWndRegEx.IsMatch(title.ToString())) //ищем окно с запросом закрытия
                        {
                            closeWndHandle = hWnd;
                            break;
                        }
                    }

                    if (closeWndHandle != null) //нашли окно с запросом закрытия
                    {
                        if (this.winState != WinState.BG_CloseWnd) //если был другой статус то устанавливаем новый
                        {
                            SetState(WinState.BG_CloseWnd);
                        }
                        else //иначе проверим не истек ли таймаут и кильнем процесс если истек.
                        {
                            //long currTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            //if ((currTimestamp - stateTimestamp) > this.closeWndTimeout)

                            long currTimestamp = DateTimeOffset.Now.Ticks;
                            double elapsedSeconds = (new TimeSpan(currTimestamp - stateTimestamp)).TotalSeconds;
                            if (elapsedSeconds * 1000 > this.closeWndTimeout )
                            {
                                Console.WriteLine("Killed PID: {0}", procObj.Id);

                                //SendEmailAsync().GetAwaiter();
                                SendEmail();

                                procObj.Kill(); //может надо поаккуратнее, но пока так.
                            }
                        }
                    }
                    else //программа на фоне и окна запроса закрытия нет
                    {
                        SetState(WinState.BG);
                    }
                }
            }

        }

        private static List<Process> ProcessByWinTitle(Regex titleRegEx)
        {
            List<Process> procList = new List<Process>();
            foreach (Process pList in Process.GetProcesses())
            {
                if (titleRegEx.IsMatch(pList.MainWindowTitle))
                {
                    procList.Add(pList);

                }
            }
            return procList;
        }

        //private static async Task SendEmailAsync()
        private static void SendEmail()
        {
            string AccountName = System.Environment.UserName.ToLower();
            string MachineName = System.Environment.MachineName;

            MailAddress from = new MailAddress(AccountName + "@tatsotsbank.ru");
            MailAddress to = new MailAddress("ri.khalfin@tatsotsbank.ru");
            MailMessage m = new MailMessage(from, to);
            m.Subject = "FinistKiller Event";
            m.Body = DateTime.Now.ToString() + " Killed Process on machine: " + MachineName;
            SmtpClient smtp = new SmtpClient("192.168.0.150", 25);
            //SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587);
            //smtp.Credentials = new NetworkCredential("somemail@gmail.com", "mypassword");
            //smtp.EnableSsl = true;

            //await smtp.SendMailAsync(m);
            smtp.Send(m);

            Console.WriteLine("Письмо отправлено");
        }
    }
}
