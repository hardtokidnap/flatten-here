using System.Runtime.InteropServices;

namespace FlattenHere;

internal static partial class Native
{
    // ----- MessageBox flags / return codes -----
    internal const uint MB_OK              = 0x00000000;
    internal const uint MB_OKCANCEL        = 0x00000001;
    internal const uint MB_YESNO           = 0x00000004;
    internal const uint MB_ICONERROR       = 0x00000010;
    internal const uint MB_ICONQUESTION    = 0x00000020;
    internal const uint MB_ICONWARNING     = 0x00000030;
    internal const uint MB_ICONINFORMATION = 0x00000040;
    internal const uint MB_SETFOREGROUND   = 0x00010000;
    internal const uint MB_TOPMOST         = 0x00040000;

    internal const int IDOK     = 1;
    internal const int IDCANCEL = 2;
    internal const int IDYES    = 6;
    internal const int IDNO     = 7;
    internal const int IDCLOSE  = 8;

    // ----- Virtual keys -----
    internal const int VK_SHIFT   = 0x10;
    internal const int VK_CONTROL = 0x11;

    // ----- ShellExecute -----
    internal const int SW_SHOWNORMAL = 1;

    // ----- TaskDialog -----
    [Flags]
    internal enum TaskDialogFlags : uint
    {
        EnableHyperlinks         = 0x0001,
        UseHIconMain             = 0x0002,
        UseHIconFooter           = 0x0004,
        AllowDialogCancellation  = 0x0008,
        UseCommandLinks          = 0x0010,
        UseCommandLinksNoIcon    = 0x0020,
        ExpandFooterArea         = 0x0040,
        ExpandedByDefault        = 0x0080,
        VerificationFlagChecked  = 0x0100,
        ShowProgressBar          = 0x0200,
        ShowMarqueeProgressBar   = 0x0400,
        CallbackTimer            = 0x0800,
        PositionRelativeToWindow = 0x1000,
        RTLLayout                = 0x2000,
        NoDefaultRadioButton     = 0x4000,
        CanBeMinimized           = 0x8000,
        SizeToContent            = 0x01000000,
    }

    [Flags]
    internal enum TaskDialogCommonButtons : uint
    {
        Ok     = 0x01,
        Yes    = 0x02,
        No     = 0x04,
        Cancel = 0x08,
        Retry  = 0x10,
        Close  = 0x20,
    }

    // Stock TaskDialog icons. In Win32, TD_WARNING_ICON is MAKEINTRESOURCEW(-1) which
    // expands to (LPWSTR)((ULONG_PTR)((WORD)(-1))) = pointer-sized value 0x000000000000FFFF.
    // Declared as positive int so casting to IntPtr does NOT sign-extend on x64.
    internal const int TD_WARNING_ICON     = 0xFFFF;
    internal const int TD_ERROR_ICON       = 0xFFFE;
    internal const int TD_INFORMATION_ICON = 0xFFFD;
    internal const int TD_SHIELD_ICON      = 0xFFFC;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    internal struct TASKDIALOG_BUTTON
    {
        public int nButtonID;
        public IntPtr pszButtonText;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    internal struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public TaskDialogFlags dwFlags;
        public TaskDialogCommonButtons dwCommonButtons;
        public IntPtr pszWindowTitle;
        public IntPtr pszMainIcon;          // union with hMainIcon; PCWSTR or HICON
        public IntPtr pszMainInstruction;
        public IntPtr pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        public IntPtr pszVerificationText;
        public IntPtr pszExpandedInformation;
        public IntPtr pszExpandedControlText;
        public IntPtr pszCollapsedControlText;
        public IntPtr pszFooterIcon;        // union with hFooterIcon
        public IntPtr pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    [LibraryImport("comctl32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int TaskDialogIndirect(
        in TASKDIALOGCONFIG pTaskConfig,
        out int pnButton,
        out int pnRadioButton,
        out int pfVerificationFlagChecked);

    // ----- MessageBox / keys / shell -----
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr ShellExecute(
        IntPtr hwnd, string? lpOperation, string lpFile, string? lpParameters,
        string? lpDirectory, int nShowCmd);

    internal static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;
}
