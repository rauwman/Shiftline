using System;
using System.Runtime.InteropServices;

namespace Shiftline.Plugin
{
    /// <summary>The few Win32 calls the overlay windows need: extended window styles for click-through and no focus.</summary>
    internal static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const int WsExTransparent = 0x00000020;   // mouse input passes through to the window below
        public const int WsExToolWindow = 0x00000080;    // not in Alt+Tab and not in the taskbar
        public const int WsExNoActivate = 0x08000000;    // never takes the focus away from the game

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static int GetExStyle(IntPtr hWnd)
        {
            return IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hWnd, GwlExStyle).ToInt64() : GetWindowLong32(hWnd, GwlExStyle);
        }

        public static void SetExStyle(IntPtr hWnd, int style)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hWnd, GwlExStyle, new IntPtr(style));
            }
            else
            {
                SetWindowLong32(hWnd, GwlExStyle, style);
            }
        }
    }
}
