using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;

namespace Lvchaxs_ZH.GenshinImpact
{
    public static class GenshinWindowActivator
    {
        private const string GENSHIN_WINDOW_CLASS = "UnityWndClass";
        private const int SW_RESTORE = 9;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOW = 5;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const uint SC_RESTORE = 0xF120;
        private static readonly Size ReferenceResolution = new Size(3840, 2160);

        // 检测点定义（参考分辨率3840x2160下的坐标）
        private static readonly Dictionary<string, CheckPoint> CheckPoints = new Dictionary<string, CheckPoint>
        {
            ["Point86_88"] = new CheckPoint(new Point(86, 88), Color.FromArgb(59, 66, 85)),
            ["Point3676_1967"] = new CheckPoint(new Point(3676, 1967), Color.FromArgb(34, 34, 34))
        };

        // 点击点定义（参考分辨率3840x2160下的坐标）
        private static readonly Dictionary<string, Point> ClickPoints = new Dictionary<string, Point>
        {
            ["Point82_2052"] = new Point(82, 2052),
            ["Point1388_1079"] = new Point(1388, 1079),
            ["Point2145_1350"] = new Point(2145, 1350),
            ["Point1920_1400"] = new Point(1920, 1400),
            ["Point2100_796"] = new Point(2100, 796),
            ["Point2010_1190"] = new Point(2010, 1190)
        };

        // OCR检测区域
        private static readonly Dictionary<string, RegionInfo> OcrRegions = new Dictionary<string, RegionInfo>
        {
            ["LoginOtherAccount"] = new RegionInfo("登录其他账号", "1772,1378,2069,1433"),
            ["Sms"] = new RegionInfo("手机短信", "1873,1564,2050,1614"),
            ["ClickEnter"] = new RegionInfo("点击进入", "1820,2012,2020,2066")
        };

        // 线程控制
        private static Thread _autoDetectThread;
        private static bool _isAutoDetectRunning = false;
        private static bool _isDetectionPaused = false;
        private static bool _isInputtingAccount = false;
        private static DateTime _lastMouseCheckTime = DateTime.MinValue;
        private static readonly TimeSpan MOUSE_CHECK_INTERVAL = TimeSpan.FromMilliseconds(200);

        // 窗口信息
        private static Point _clientTopLeft = Point.Empty;
        private static Size _clientSize = Size.Empty;
        private static bool _hasWindowInfo = false;
        private static IntPtr _currentWindowHandle = IntPtr.Zero;
        private static bool _isWindowFocused = false;
        private static bool _isMouseInWindow = false;

        // 账号信息
        private static string _currentLoginUsername;
        private static string _currentLoginPassword;
        private static bool _shouldInputAccount = false;

        // 阶段标识
        private static int _currentStage = 1; // 1:第一阶段, 2:第二阶段, 3:第三阶段

        // 事件
        public static event Action<bool> LoginCompleted;

        #region Native Methods
        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PtInRect(ref RECT lprc, POINT pt);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
        #endregion

        #region 公共方法
        public static void ExecuteLogin(string username = null, string password = null)
        {
            try
            {
                InitializeLoginInfo(username, password);
                IntPtr hWnd = GetOrStartGameWindow();

                if (hWnd != IntPtr.Zero && ActivateWindow(hWnd))
                {
                    InitializeWindow(hWnd);
                    StartAutoDetect(hWnd);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[登录] 执行登录异常: {ex.Message}");
            }
        }

        public static void StopAutoDetect()
        {
            try
            {
                _isAutoDetectRunning = false;
                _isDetectionPaused = false;
                _currentWindowHandle = IntPtr.Zero;
                _currentStage = 1;

                if (_autoDetectThread != null && _autoDetectThread.IsAlive)
                {
                    _autoDetectThread.Join(1000);
                }

                Debug.WriteLine("[自动检测] 停止自动检测线程");
            }
            catch { }
        }

        public static Point ConvertCoordinate(int refX, int refY)
        {
            if (!_hasWindowInfo)
            {
                Debug.WriteLine("[坐标转换] 警告：窗口信息未初始化！");
                return Point.Empty;
            }

            try
            {
                double scaleX = (double)_clientSize.Width / ReferenceResolution.Width;
                double scaleY = (double)_clientSize.Height / ReferenceResolution.Height;

                int screenX = _clientTopLeft.X + (int)(refX * scaleX);
                int screenY = _clientTopLeft.Y + (int)(refY * scaleY);

                Debug.WriteLine($"[坐标转换] 参考({refX},{refY}) -> 屏幕({screenX},{screenY})");

                return new Point(screenX, screenY);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[坐标转换] 错误: {ex.Message}");
                return Point.Empty;
            }
        }

        public static bool HasWindowInfo => _hasWindowInfo;
        #endregion

        #region 私有方法 - 初始化
        private static void InitializeLoginInfo(string username, string password)
        {
            _currentLoginUsername = username;
            _currentLoginPassword = password;
            _shouldInputAccount = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
            _isInputtingAccount = false;
            _currentStage = 1;
            _isWindowFocused = false;
            _isDetectionPaused = false;

            Debug.WriteLine($"[登录] 账号信息已设置");
        }

        private static IntPtr GetOrStartGameWindow()
        {
            IntPtr hWnd = FindWindow(GENSHIN_WINDOW_CLASS, null);

            if (hWnd == IntPtr.Zero)
            {
                if (!StartGame() || !WaitForWindow(15)) return IntPtr.Zero;
                hWnd = FindWindow(GENSHIN_WINDOW_CLASS, null);
            }

            return hWnd;
        }

        private static void InitializeWindow(IntPtr hWnd)
        {
            Thread.Sleep(100);
            GetResolutionInfo(hWnd);
            InitializeOCRProcessor();
            MoveMouseToWindowCenter(hWnd);
        }
        #endregion

        #region 私有方法 - 游戏启动
        private static bool WaitForWindow(int maxWaitSeconds)
        {
            for (int i = 0; i < maxWaitSeconds; i++)
            {
                Thread.Sleep(1000);
                if (FindWindow(GENSHIN_WINDOW_CLASS, null) != IntPtr.Zero) return true;
            }
            return false;
        }

        private static bool StartGame()
        {
            try
            {
                string userGamePath = LoadUserGamePath();
                if (!string.IsNullOrEmpty(userGamePath) && File.Exists(userGamePath))
                {
                    Debug.WriteLine($"[启动游戏] 使用用户设置路径: {userGamePath}");
                    return StartGameFromPath(userGamePath);
                }

                string folder = FindGenshinFolder();
                if (string.IsNullOrEmpty(folder)) return false;

                string gameFolder = Path.Combine(folder, "Genshin Impact Game");
                string exePath = Path.Combine(gameFolder, "YuanShen.exe");

                return StartGameFromPath(exePath);
            }
            catch { return false; }
        }

        private static string LoadUserGamePath()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_path.json");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using var document = System.Text.Json.JsonDocument.Parse(json);

                    if (document.RootElement.TryGetProperty("GamePath", out var pathElement))
                    {
                        return pathElement.GetString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool StartGameFromPath(string exePath)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    Debug.WriteLine($"[启动游戏] 游戏文件不存在: {exePath}");
                    return false;
                }

                string gameFolder = Path.GetDirectoryName(exePath);

                Debug.WriteLine($"[启动游戏] 启动游戏: {exePath}");
                Debug.WriteLine($"[启动游戏] 工作目录: {gameFolder}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = gameFolder
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[启动游戏] 启动失败: {ex.Message}");
                return false;
            }
        }

        private static string FindGenshinFolder()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                try
                {
                    // 检查根目录
                    string[] rootDirs = Directory.GetDirectories(drive.RootDirectory.FullName, "*Genshin Impact*", SearchOption.TopDirectoryOnly);
                    foreach (string dir in rootDirs)
                        if (dir.Contains("Genshin Impact")) return dir;

                    // 检查程序目录
                    string[] programPaths = { "Program Files", "Program Files (x86)", "Games" };
                    foreach (string programPath in programPaths)
                    {
                        string fullPath = Path.Combine(drive.RootDirectory.FullName, programPath);
                        if (Directory.Exists(fullPath))
                        {
                            string result = SearchDirectory(fullPath, "Genshin Impact");
                            if (!string.IsNullOrEmpty(result)) return result;
                        }
                    }
                }
                catch { continue; }
            }
            return null;
        }

        private static string SearchDirectory(string directory, string folderName)
        {
            try
            {
                // 搜索当前目录
                string[] directories = Directory.GetDirectories(directory, $"*{folderName}*", SearchOption.TopDirectoryOnly);
                foreach (string dir in directories)
                    if (dir.EndsWith(folderName, StringComparison.OrdinalIgnoreCase)) return dir;

                // 递归搜索子目录
                foreach (string subDir in Directory.GetDirectories(directory))
                {
                    if (subDir.Contains("Windows") || subDir.Contains("$")) continue;
                    string result = SearchDirectory(subDir, folderName);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            catch { }
            return null;
        }
        #endregion

        #region 私有方法 - 窗口操作
        private static bool ActivateWindow(IntPtr hWnd)
        {
            try
            {
                if (IsIconic(hWnd))
                {
                    SendMessage(hWnd, WM_SYSCOMMAND, (IntPtr)SC_RESTORE, IntPtr.Zero);
                    Thread.Sleep(300);
                    ShowWindow(hWnd, SW_RESTORE);
                    ShowWindow(hWnd, SW_SHOWNORMAL);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                Thread.Sleep(200);
                return SetForegroundWindow(hWnd) || BringWindowToTop(hWnd);
            }
            catch { return false; }
        }

        private static void GetResolutionInfo(IntPtr hWnd)
        {
            try
            {
                if (!GetClientRect(hWnd, out RECT clientRect))
                {
                    Debug.WriteLine("[窗口信息] 获取客户区矩形失败");
                    return;
                }

                POINT clientTopLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };

                if (!ClientToScreen(hWnd, ref clientTopLeft))
                {
                    Debug.WriteLine("[窗口信息] 转换客户区坐标失败");
                    return;
                }

                _clientTopLeft = new Point(clientTopLeft.X, clientTopLeft.Y);
                _clientSize = new Size(clientRect.Width, clientRect.Height);
                _hasWindowInfo = true;

                Debug.WriteLine($"[窗口信息] 客户区位置: {_clientTopLeft}, 大小: {_clientSize}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口信息] 获取分辨率信息失败: {ex.Message}");
            }
        }

        private static void MoveMouseToWindowCenter(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !_hasWindowInfo) return;

                int centerX = _clientTopLeft.X + (_clientSize.Width / 2);
                int centerY = _clientTopLeft.Y + (_clientSize.Height / 2);

                SetCursorPos(centerX, centerY);
                Debug.WriteLine($"[鼠标移动] 移动到窗口中心: X={centerX}, Y={centerY}");
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[鼠标移动] 移动到中心失败: {ex.Message}");
            }
        }
        #endregion

        #region 私有方法 - OCR处理
        private static void InitializeOCRProcessor()
        {
            try
            {
                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                if (Directory.Exists(tessDataPath))
                {
                    GenshinOCRProcessor.Initialize(tessDataPath);
                    Debug.WriteLine($"[OCR初始化] 使用自定义路径: {tessDataPath}");
                }
                else
                {
                    GenshinOCRProcessor.Initialize();
                    Debug.WriteLine($"[OCR初始化] 使用默认路径");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR初始化] 失败: {ex.Message}");
            }
        }
        #endregion

        #region 私有方法 - 焦点检测
        private static bool IsWindowFocused()
        {
            if (_currentWindowHandle == IntPtr.Zero) return false;

            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                return foregroundWindow == _currentWindowHandle;
            }
            catch { return false; }
        }

        private static bool IsMouseInWindow()
        {
            if (_currentWindowHandle == IntPtr.Zero || !_hasWindowInfo) return false;

            try
            {
                POINT mousePoint;
                if (!GetCursorPos(out mousePoint)) return false;

                var clientRect = new RECT
                {
                    Left = _clientTopLeft.X,
                    Top = _clientTopLeft.Y,
                    Right = _clientTopLeft.X + _clientSize.Width,
                    Bottom = _clientTopLeft.Y + _clientSize.Height
                };

                return PtInRect(ref clientRect, mousePoint);
            }
            catch { return false; }
        }

        private static void UpdateWindowFocusState()
        {
            _isWindowFocused = IsWindowFocused();

            if (DateTime.Now - _lastMouseCheckTime >= MOUSE_CHECK_INTERVAL)
            {
                _isMouseInWindow = IsMouseInWindow();
                _lastMouseCheckTime = DateTime.Now;
            }

            bool shouldPauseDetection = !_isWindowFocused || !_isMouseInWindow;

            if (_isDetectionPaused != shouldPauseDetection)
            {
                _isDetectionPaused = shouldPauseDetection;
                Debug.WriteLine($"[焦点检测] 检测{(shouldPauseDetection ? "已暂停" : "已恢复")} - 窗口焦点: {_isWindowFocused}, 鼠标在窗口内: {_isMouseInWindow}");
            }
        }
        #endregion

        #region 私有方法 - 自动检测
        private static void StartAutoDetect(IntPtr hWnd)
        {
            try
            {
                _currentWindowHandle = hWnd;
                _isAutoDetectRunning = true;

                if (_autoDetectThread != null && _autoDetectThread.IsAlive)
                {
                    _autoDetectThread.Join(200);
                }

                _autoDetectThread = new Thread(AutoDetectLoop)
                {
                    IsBackground = true,
                    Name = "AutoDetectThread"
                };
                _autoDetectThread.Start();

                Debug.WriteLine("[自动检测] 启动自动检测线程");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动检测] 启动线程失败: {ex.Message}");
            }
        }

        private static void AutoDetectLoop()
        {
            try
            {
                Debug.WriteLine("[自动检测] 开始自动检测循环");

                while (_isAutoDetectRunning && _currentWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        UpdateWindowFocusState();

                        if (_isDetectionPaused || _isInputtingAccount)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        switch (_currentStage)
                        {
                            case 1:
                                HandleStage1();
                                break;
                            case 2:
                                HandleStage2();
                                break;
                            case 3:
                                HandleStage3();
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[自动检测] 错误: {ex.Message}");
                        int waitTime = _currentStage == 1 ? 1500 : 1000;
                        Thread.Sleep(waitTime);
                    }
                }

                Debug.WriteLine("[自动检测] 结束");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动检测] 循环异常: {ex.Message}");
                OnLoginCompleted(false);
            }
        }

        private static void OnLoginCompleted(bool success)
        {
            LoginCompleted?.Invoke(success);
        }
        #endregion

        #region 私有方法 - 阶段处理
        private static void HandleStage1()
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 第一阶段 - 检测所有目标");

            bool hasDetectedTarget = DetectStage1Targets();

            if (!hasDetectedTarget)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 第一阶段未检测到任何目标，按ESC");
                PressKey(Keys.Escape);
            }

            Thread.Sleep(1500);
        }

        private static bool DetectStage1Targets()
        {
            // 检测坐标86,88
            if (CheckColorAtPoint(CheckPoints["Point86_88"]))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到坐标86,88，点击82,2052");
                ClickAtPoint(ClickPoints["Point82_2052"]);

                Thread.Sleep(300);
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 直接点击1388,1079");
                ClickAtPoint(ClickPoints["Point1388_1079"]);

                _currentStage = 2;
                return true;
            }

            // 检测坐标3676,1967
            if (CheckColorAtPoint(CheckPoints["Point3676_1967"]))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到坐标3676,1967，点击该位置");
                ClickAtPoint(ClickPoints["Point3676_1967"]);

                Thread.Sleep(300);
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 点击2145,1350坐标");
                ClickAtPoint(ClickPoints["Point2145_1350"]);

                _currentStage = 2;
                return true;
            }

            // OCR检测
            return ProcessOCRDetection(OcrRegions["LoginOtherAccount"], OcrRegions["Sms"]);
        }

        private static void HandleStage2()
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 第二阶段 - 同时检测三个目标");

            if (CheckColorAtPoint(CheckPoints["Point3676_1967"]))
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到坐标3676,1967，点击该位置");
                ClickAtPoint(CheckPoints["Point3676_1967"].Point);

                Thread.Sleep(300);
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 点击2145,1350坐标");
                ClickAtPoint(ClickPoints["Point2145_1350"]);

                Thread.Sleep(500);
                return;
            }

            ProcessOCRDetection(OcrRegions["LoginOtherAccount"], OcrRegions["Sms"]);
        }

        private static void HandleStage3()
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 第三阶段 - 检测'点击进入'文本");

            var region = OcrRegions["ClickEnter"];
            var rect = region.GetCaptureRect();

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                Debug.WriteLine($"[第三阶段] 获取点击进入区域失败");
                return;
            }

            Debug.WriteLine($"[第三阶段] 捕获点击进入区域: X={rect.X}, Y={rect.Y}, Width={rect.Width}, Height={rect.Height}");

            using (var screenshot = DxgiSimpleCapture.CaptureScreenArea(rect.X, rect.Y, rect.Width, rect.Height))
            {
                if (screenshot == null)
                {
                    Debug.WriteLine("[第三阶段] 截图失败");
                    return;
                }

                using (var grayscaleImage = ConvertToGrayscale(screenshot))
                {
                    string ocrResult = GenshinOCRProcessor.RecognizeTextDirect(grayscaleImage ?? screenshot);

                    if (!string.IsNullOrEmpty(ocrResult))
                    {
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] OCR识别 [点击进入]: {ocrResult}");

                        bool isClickEnter = ocrResult.Contains("点击进入") ||
                                            ocrResult.Contains("击进入") ||
                                            ocrResult.Contains("进入");

                        if (isClickEnter)
                        {
                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到'点击进入'文本，执行点击");
                            ClickAtWindowCenter();
                            OnLoginCompleted(true);
                            _isAutoDetectRunning = false;
                        }
                    }
                }
            }
        }

        private static void ClickAtWindowCenter()
        {
            if (_hasWindowInfo)
            {
                int centerX = _clientTopLeft.X + (_clientSize.Width / 2);
                int centerY = _clientTopLeft.Y + (_clientSize.Height / 2);

                SetCursorPos(centerX, centerY);
                Thread.Sleep(10);

                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已点击客户区中心位置: X={centerX}, Y={centerY}");
            }
            else
            {
                ClickAtCurrentPosition();
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 窗口信息不可用，在当前位置点击");
            }
        }
        #endregion

        #region 私有方法 - OCR检测
        private static bool ProcessOCRDetection(RegionInfo loginRegion, RegionInfo smsRegion)
        {
            var regions = new List<RegionInfo> { loginRegion, smsRegion };
            var screenshots = CaptureAllRegionsSimultaneously(regions);

            foreach (var screenshot in screenshots)
            {
                try
                {
                    using (var grayscaleImage = ConvertToGrayscale(screenshot.OriginalScreenshot))
                    {
                        string ocrResult = GenshinOCRProcessor.RecognizeTextDirect(
                            grayscaleImage ?? screenshot.OriginalScreenshot);

                        if (!string.IsNullOrEmpty(ocrResult))
                        {
                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] OCR识别 [{screenshot.Region.Name}]: {ocrResult}");

                            if (HandleOCRResult(screenshot.Region, ocrResult))
                                return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OCR处理] 错误: {ex.Message}");
                }
                finally
                {
                    screenshot.OriginalScreenshot.Dispose();
                }
            }

            return false;
        }

        private static bool HandleOCRResult(RegionInfo region, string ocrResult)
        {
            bool isLoginOtherAccount = region.Name.Contains("登录其他账号") &&
                                       (ocrResult.Contains("登录其他账号") ||
                                        ocrResult.Contains("录其他账号") ||
                                        ocrResult.Contains("其他账号") ||
                                        ocrResult.Contains("登录其他") ||
                                        ocrResult.Contains("其他") ||
                                        ocrResult.ToLower().Contains("login"));

            bool isSMS = region.Name.Contains("手机短信") &&
                         (ocrResult.Contains("手机短信") ||
                          ocrResult.Contains("机短信") ||
                          ocrResult.Contains("短信") ||
                          ocrResult.Contains("手机") ||
                          ocrResult.Contains("手短信") ||
                          ocrResult.ToLower().Contains("sms"));

            if (isLoginOtherAccount)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到'登录其他账号'，点击1920,1400");
                ClickAtPoint(ClickPoints["Point1920_1400"]);

                Thread.Sleep(200);
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 点击2100,796坐标");
                ClickAtPoint(ClickPoints["Point2100_796"]);

                InputAccountAndPassword();
                _currentStage = 3;
                return true;
            }
            else if (isSMS)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到'手机短信'，点击2100,796");
                ClickAtPoint(ClickPoints["Point2100_796"]);

                InputAccountAndPassword();
                _currentStage = 3;
                return true;
            }

            return false;
        }

        private static List<(RegionInfo Region, Bitmap OriginalScreenshot)> CaptureAllRegionsSimultaneously(List<RegionInfo> regions)
        {
            var results = new List<(RegionInfo Region, Bitmap OriginalScreenshot)>();
            var lockObj = new object();

            Parallel.ForEach(regions, region =>
            {
                try
                {
                    Rectangle rect = region.GetCaptureRect();
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        Debug.WriteLine($"[截图] 区域{region.Name}获取矩形失败: {rect}");
                        return;
                    }

                    Debug.WriteLine($"[截图] 捕获区域{region.Name}: X={rect.X}, Y={rect.Y}, Width={rect.Width}, Height={rect.Height}");

                    Bitmap screenshot = DxgiSimpleCapture.CaptureScreenArea(
                        rect.X, rect.Y, rect.Width, rect.Height);

                    if (screenshot != null)
                    {
                        lock (lockObj)
                        {
                            results.Add((region, screenshot));
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[截图] 区域{region.Name}截图失败");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[截图] 区域{region.Name}错误: {ex.Message}");
                }
            });

            return results;
        }
        #endregion

        #region 私有方法 - 账号输入
        private static void InputAccountAndPassword()
        {
            try
            {
                if (!_shouldInputAccount || string.IsNullOrEmpty(_currentLoginUsername) ||
                    string.IsNullOrEmpty(_currentLoginPassword))
                {
                    Debug.WriteLine("[输入账号] 没有账号信息，跳过输入");
                    _currentStage = 3;
                    return;
                }

                _isInputtingAccount = true;
                Debug.WriteLine($"[输入账号] 开始输入账号");
                Thread.Sleep(500);

                // 粘贴账号
                PasteText(_currentLoginUsername);
                Debug.WriteLine($"[输入账号] 已粘贴账号");

                Thread.Sleep(500);
                PressKey(Keys.Tab);
                Debug.WriteLine($"[输入账号] 已按下TAB键切换到密码输入框");

                Thread.Sleep(100);
                PasteText(_currentLoginPassword);
                Debug.WriteLine($"[输入账号] 已粘贴密码");

                Thread.Sleep(100);
                PressKey(Keys.Enter);
                Debug.WriteLine($"[输入账号] 已按下ENTER键");

                Thread.Sleep(100);
                ClickAtPoint(ClickPoints["Point2010_1190"]);
                Debug.WriteLine($"[输入账号] 已点击登录按钮 (2010,1190)");

                Thread.Sleep(500);
                Debug.WriteLine("[输入账号] 账号密码输入完成");

                _currentStage = 3;
                Debug.WriteLine("[输入账号] 进入第三阶段检测");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[输入账号] 输入账号密码失败: {ex.Message}");
                _currentStage = 3;
            }
            finally
            {
                _isInputtingAccount = false;
            }
        }

        private static void PasteText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;

                bool success = NativeClipboardHelper.SafeSetText(text);
                if (!success)
                {
                    Thread.Sleep(50);
                    success = NativeClipboardHelper.SafeSetText(text);
                    if (!success) return;
                }

                Debug.WriteLine($"[粘贴文本] 剪贴板设置成功");
                Thread.Sleep(50);

                keybd_event((byte)Keys.Control, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                keybd_event((byte)Keys.V, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                Thread.Sleep(10);
                keybd_event((byte)Keys.V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event((byte)Keys.Control, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[粘贴文本] 粘贴失败: {ex.Message}");
            }
        }
        #endregion

        #region 私有方法 - 基础操作
        private static bool CheckColorAtPoint(CheckPoint checkPoint)
        {
            if (_currentWindowHandle == IntPtr.Zero || !_hasWindowInfo)
            {
                Debug.WriteLine($"[颜色检测] 窗口信息未初始化");
                return false;
            }

            try
            {
                var screenPoint = ConvertCoordinate(checkPoint.Point.X, checkPoint.Point.Y);
                if (screenPoint == Point.Empty) return false;

                Debug.WriteLine($"[颜色检测] 检测点 ({checkPoint.Point.X},{checkPoint.Point.Y}) -> 屏幕 ({screenPoint.X},{screenPoint.Y})");
                Color detectedColor = GetPixelColor(screenPoint.X, screenPoint.Y);

                bool match = detectedColor.R == checkPoint.Color.R &&
                           detectedColor.G == checkPoint.Color.G &&
                           detectedColor.B == checkPoint.Color.B;

                Debug.WriteLine($"[颜色检测] 目标颜色: {checkPoint.Color}, 检测颜色: {detectedColor}, 匹配: {match}");

                return match;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[颜色检测] 错误: {ex.Message}");
                return false;
            }
        }

        private static Color GetPixelColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                uint pixel = GetPixel(hdc, x, y);
                return Color.FromArgb(
                    (int)(pixel & 0x000000FF),
                    (int)((pixel & 0x0000FF00) >> 8),
                    (int)((pixel & 0x00FF0000) >> 16));
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        private static void PressKey(Keys key)
        {
            try
            {
                byte keyCode = (byte)key;
                keybd_event(keyCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                Thread.Sleep(50);
                keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        private static void ClickAtPoint(Point point)
        {
            try
            {
                if (!_hasWindowInfo)
                {
                    Debug.WriteLine($"[点击] 窗口信息未初始化");
                    return;
                }

                var screenPoint = ConvertCoordinate(point.X, point.Y);
                if (screenPoint == Point.Empty) return;

                Debug.WriteLine($"[点击] 点击点 ({point.X},{point.Y}) -> 屏幕 ({screenPoint.X},{screenPoint.Y})");

                SetCursorPos(screenPoint.X, screenPoint.Y);
                Thread.Sleep(10);

                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[点击] 错误: {ex.Message}");
            }
        }

        private static void ClickAtCurrentPosition()
        {
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                Debug.WriteLine("[鼠标点击] 在当前位置点击左键");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[鼠标点击] 点击失败: {ex.Message}");
            }
        }

        private static Bitmap ConvertToGrayscale(Bitmap original)
        {
            if (original == null) return null;

            try
            {
                Bitmap grayscale = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);

                using (Graphics g = Graphics.FromImage(grayscale))
                {
                    ColorMatrix colorMatrix = new ColorMatrix(
                        new float[][]
                        {
                            new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                            new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                            new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {0, 0, 0, 0, 1}
                        });

                    ImageAttributes attributes = new ImageAttributes();
                    attributes.SetColorMatrix(colorMatrix);

                    g.DrawImage(original,
                        new Rectangle(0, 0, original.Width, original.Height),
                        0, 0, original.Width, original.Height,
                        GraphicsUnit.Pixel, attributes);
                }

                return grayscale;
            }
            catch { return original; }
        }
        #endregion

        #region 内部类
        private class CheckPoint
        {
            public Point Point { get; }
            public Color Color { get; }

            public CheckPoint(Point point, Color color)
            {
                Point = point;
                Color = color;
            }
        }

        private class RegionInfo
        {
            public string Name { get; }
            public string Coordinates { get; }

            public RegionInfo(string name, string coordinates)
            {
                Name = name;
                Coordinates = coordinates;
            }

            public Rectangle GetCaptureRect()
            {
                var parts = Coordinates.Split(',');
                if (parts.Length != 4)
                {
                    Debug.WriteLine($"[区域] 坐标格式错误: {Coordinates}");
                    return Rectangle.Empty;
                }

                try
                {
                    int refLeft = int.Parse(parts[0].Trim());
                    int refTop = int.Parse(parts[1].Trim());
                    int refRight = int.Parse(parts[2].Trim());
                    int refBottom = int.Parse(parts[3].Trim());

                    Debug.WriteLine($"[区域] {Name}: 参考坐标({refLeft},{refTop})-({refRight},{refBottom})");

                    Point topLeft = ConvertCoordinate(refLeft, refTop);
                    Point bottomRight = ConvertCoordinate(refRight, refBottom);

                    if (topLeft == Point.Empty || bottomRight == Point.Empty)
                    {
                        Debug.WriteLine($"[区域] {Name}: 坐标转换失败");
                        return Rectangle.Empty;
                    }

                    Rectangle rect = new Rectangle(
                        topLeft.X,
                        topLeft.Y,
                        bottomRight.X - topLeft.X,
                        bottomRight.Y - topLeft.Y);

                    Debug.WriteLine($"[区域] {Name}: 屏幕矩形({rect.X},{rect.Y},{rect.Width},{rect.Height})");

                    return rect;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[区域] {Name}解析错误: {ex.Message}");
                    return Rectangle.Empty;
                }
            }
        }

        private enum Keys : byte
        {
            Escape = 0x1B,
            Z = 0x5A,
            Tab = 0x09,
            Enter = 0x0D,
            Control = 0x11,
            V = 0x56
        }
        #endregion
    }
}