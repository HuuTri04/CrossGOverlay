using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CrossGOverlay;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        int currentExtendedStyle = GetWindowLong(windowHandle, GwlExStyle);
        SetWindowLong(windowHandle, GwlExStyle, currentExtendedStyle | WsExTransparent | WsExToolWindow);
    }
}