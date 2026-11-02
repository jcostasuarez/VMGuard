using System;
using System.Runtime.InteropServices;

namespace VMGuard
{
    internal static class NativeMethods
    {
        public const int WM_QUERYENDSESSION = 0x0011;
        public const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

        /// <summary>Pide que este proceso reciba el aviso de apagado antes que la mayoria (0x3FF = maximo para apps).</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);
    }
}
