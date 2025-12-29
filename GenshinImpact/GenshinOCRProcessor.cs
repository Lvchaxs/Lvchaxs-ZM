using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using Tesseract;

namespace Lvchaxs_ZH.GenshinImpact
{
    public static class GenshinOCRProcessor
    {
        private static string tessDataPath = null;
        private static bool isInitialized = false;
        private static readonly object initLock = new object();

        public static bool Initialize(string customTessDataPath = null)
        {
            lock (initLock)
            {
                try
                {
                    if (isInitialized && !string.IsNullOrEmpty(tessDataPath))
                        return true;

                    if (!string.IsNullOrEmpty(customTessDataPath))
                    {
                        tessDataPath = customTessDataPath;
                    }
                    else
                    {
                        tessDataPath = FindTessDataPath();
                    }

                    if (string.IsNullOrEmpty(tessDataPath) || !Directory.Exists(tessDataPath))
                    {
                        throw new DirectoryNotFoundException($"未找到 tessdata 目录: {tessDataPath}");
                    }

                    string chiSimPath = Path.Combine(tessDataPath, "chi_sim.traineddata");
                    if (!File.Exists(chiSimPath))
                    {
                        throw new FileNotFoundException($"未找到简体中文语言文件: {chiSimPath}");
                    }

                    // 只进行简单测试而不创建完整的引擎实例
                    isInitialized = true;
                    Debug.WriteLine($"[OCR] 初始化成功，语言文件路径: {tessDataPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OCR] 初始化失败: {ex.Message}");
                    isInitialized = false;
                    return false;
                }
            }
        }

        private static void ConfigureTesseractEngine(TesseractEngine engine)
        {
            try
            {
                engine.SetVariable("tessedit_pageseg_mode", "7");
                engine.SetVariable("load_system_dawg", "0");
                engine.SetVariable("load_freq_dawg", "0");
                engine.SetVariable("user_defined_dpi", "96");
                engine.SetVariable("tessedit_minimal_confidence", "60");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR] 配置引擎参数失败: {ex.Message}");
            }
        }

        public static string RecognizeTextDirect(Bitmap bitmap, string language = "chi_sim")
        {
            if (bitmap == null)
            {
                Debug.WriteLine("[OCR] 错误：bitmap 为 null");
                return string.Empty;
            }

            if (!isInitialized)
            {
                if (!Initialize())
                {
                    Debug.WriteLine("[OCR] 错误：OCR 未初始化");
                    return string.Empty;
                }
            }

            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    // 使用完全限定的 System.Drawing.Imaging.ImageFormat
                    bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] imageData = memoryStream.ToArray();

                    using (var pix = Pix.LoadFromMemory(imageData))
                    using (var engine = new TesseractEngine(tessDataPath, language, EngineMode.LstmOnly))
                    {
                        ConfigureTesseractEngine(engine);

                        using (var page = engine.Process(pix))
                        {
                            string rawText = page.GetText();
                            string cleanedText = CleanOCRText(rawText);
                            return cleanedText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR] 内存识别失败: {ex.Message}");
                return string.Empty;
            }
        }

        private static string CleanOCRText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Trim();

            // 移除中文字符间的空格
            text = Regex.Replace(text, @"([\u4e00-\u9fa5])\s+([\u4e00-\u9fa5])", "$1$2");

            // 移除所有空白字符
            text = Regex.Replace(text, @"\s+", "");

            // 移除特定干扰字符
            text = text.Replace("|", "")
                       .Replace("_", "")
                       .Replace("~", "")
                       .Replace("`", "")
                       .Replace("\"", "")
                       .Replace("'", "");

            return text;
        }

        private static string FindTessDataPath()
        {
            string[] possiblePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"),
                Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tessdata"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "tessdata"),
                @"C:\Program Files\Tesseract-OCR\tessdata",
                @"C:\Program Files (x86)\Tesseract-OCR\tessdata"
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                    {
                        string chiSimFile = Path.Combine(fullPath, "chi_sim.traineddata");
                        if (File.Exists(chiSimFile))
                        {
                            Debug.WriteLine($"[OCR] 找到 tessdata 目录: {fullPath}");
                            return fullPath;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
    }
}