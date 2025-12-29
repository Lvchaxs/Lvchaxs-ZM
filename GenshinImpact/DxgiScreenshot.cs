using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Lvchaxs_ZH.GenshinImpact
{
    public static class DxgiSimpleCapture
    {
        #region GDI 函数
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
                                          IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        #endregion

        private const int SRCCOPY = 0x00CC0020;

        public static Bitmap CaptureScreenArea(int x, int y, int width, int height)
        {
            try
            {
                IntPtr hDesktop = GetDesktopWindow();
                if (hDesktop == IntPtr.Zero)
                    return null;

                IntPtr hdcScreen = GetDC(hDesktop);
                if (hdcScreen == IntPtr.Zero)
                {
                    ReleaseDC(hDesktop, hdcScreen);
                    return null;
                }

                IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
                if (hdcMem == IntPtr.Zero)
                {
                    ReleaseDC(hDesktop, hdcScreen);
                    return null;
                }

                IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    DeleteDC(hdcMem);
                    ReleaseDC(hDesktop, hdcScreen);
                    return null;
                }

                IntPtr hOldBitmap = SelectObject(hdcMem, hBitmap);
                if (hOldBitmap == IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                    DeleteDC(hdcMem);
                    ReleaseDC(hDesktop, hdcScreen);
                    return null;
                }

                bool success = BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY);

                if (!success)
                {
                    SelectObject(hdcMem, hOldBitmap);
                    DeleteObject(hBitmap);
                    DeleteDC(hdcMem);
                    ReleaseDC(hDesktop, hdcScreen);
                    return null;
                }

                Bitmap bitmap = Image.FromHbitmap(hBitmap);

                SelectObject(hdcMem, hOldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
                ReleaseDC(hDesktop, hdcScreen);

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static bool CaptureArea(int refLeft, int refTop, int refRight, int refBottom, string fileName = null)
        {
            try
            {
                if (!GenshinWindowActivator.HasWindowInfo)
                {
                    return false;
                }

                Point topLeft = GenshinWindowActivator.ConvertCoordinate(refLeft, refTop);
                Point bottomRight = GenshinWindowActivator.ConvertCoordinate(refRight, refBottom);

                int width = bottomRight.X - topLeft.X;
                int height = bottomRight.Y - topLeft.Y;

                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                using (Bitmap screenshot = CaptureScreenArea(topLeft.X, topLeft.Y, width, height))
                {
                    if (screenshot == null)
                    {
                        return false;
                    }

                    string screenshotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
                    if (!Directory.Exists(screenshotDir))
                        Directory.CreateDirectory(screenshotDir);

                    string filePath = Path.Combine(screenshotDir, fileName ?? $"genshin_gdi_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    try
                    {
                        screenshot.Save(filePath, ImageFormat.Png);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}