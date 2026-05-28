using System.Runtime.InteropServices;

namespace FlattenHere;

internal readonly record struct TaskDialogResult(int ButtonId, bool VerificationChecked);

// Fluent builder over TaskDialogIndirect. Lives next to Native.cs to keep the Win32
// shape readable. AOT-safe: all strings marshalled into HGlobal blocks, freed in Dispose.
internal sealed class TaskDialogBuilder : IDisposable
{
    private Native.TASKDIALOGCONFIG _config;
    private readonly List<IntPtr> _strings = new();
    private readonly List<Native.TASKDIALOG_BUTTON> _buttons = new();
    private IntPtr _buttonsBlock = IntPtr.Zero;

    public TaskDialogBuilder()
    {
        _config = new Native.TASKDIALOGCONFIG
        {
            cbSize = (uint)Marshal.SizeOf<Native.TASKDIALOGCONFIG>(),
            dwFlags = Native.TaskDialogFlags.AllowDialogCancellation
                    | Native.TaskDialogFlags.SizeToContent,
        };
    }

    public TaskDialogBuilder WithTitle(string s)              { _config.pszWindowTitle       = M(s); return this; }
    public TaskDialogBuilder WithMainInstruction(string s)    { _config.pszMainInstruction   = M(s); return this; }
    public TaskDialogBuilder WithContent(string s)            { _config.pszContent           = M(s); return this; }
    public TaskDialogBuilder WithFooter(string s)             { _config.pszFooter            = M(s); return this; }
    public TaskDialogBuilder WithExpandedInfo(string s)       { _config.pszExpandedInformation = M(s); return this; }
    public TaskDialogBuilder WithExpandLabels(string collapsed, string expanded)
    {
        _config.pszCollapsedControlText = M(collapsed);
        _config.pszExpandedControlText = M(expanded);
        return this;
    }

    public TaskDialogBuilder WithStockIcon(int stockIcon)
    {
        _config.pszMainIcon = (IntPtr)stockIcon;
        return this;
    }

    public TaskDialogBuilder WithVerification(string text, bool defaultChecked = false)
    {
        _config.pszVerificationText = M(text);
        if (defaultChecked)
            _config.dwFlags |= Native.TaskDialogFlags.VerificationFlagChecked;
        return this;
    }

    public TaskDialogBuilder WithCommonButtons(Native.TaskDialogCommonButtons b)
    {
        _config.dwCommonButtons = b;
        return this;
    }

    public TaskDialogBuilder AddCommandLink(int id, string label, string description)
    {
        // Command-link buttons render with a bold title + smaller description; \n separator.
        var text = string.IsNullOrEmpty(description) ? label : $"{label}\n{description}";
        _buttons.Add(new Native.TASKDIALOG_BUTTON { nButtonID = id, pszButtonText = M(text) });
        _config.dwFlags |= Native.TaskDialogFlags.UseCommandLinks;
        return this;
    }

    public TaskDialogBuilder AddButton(int id, string label)
    {
        _buttons.Add(new Native.TASKDIALOG_BUTTON { nButtonID = id, pszButtonText = M(label) });
        return this;
    }

    public TaskDialogBuilder WithDefaultButton(int id)
    {
        _config.nDefaultButton = id;
        return this;
    }

    public TaskDialogBuilder EnableHyperlinks()
    {
        _config.dwFlags |= Native.TaskDialogFlags.EnableHyperlinks;
        return this;
    }

    public TaskDialogResult Show()
    {
        if (_buttons.Count > 0)
        {
            var size = Marshal.SizeOf<Native.TASKDIALOG_BUTTON>();
            _buttonsBlock = Marshal.AllocHGlobal(size * _buttons.Count);
            for (int i = 0; i < _buttons.Count; i++)
                Marshal.StructureToPtr(_buttons[i], _buttonsBlock + size * i, fDeleteOld: false);
            _config.pButtons = _buttonsBlock;
            _config.cButtons = (uint)_buttons.Count;
        }

        var hr = Native.TaskDialogIndirect(in _config, out int button, out _, out int verified);
        if (hr != 0)
            throw new InvalidOperationException($"TaskDialogIndirect failed (HRESULT 0x{hr:X8}).");

        return new TaskDialogResult(button, verified != 0);
    }

    private IntPtr M(string s)
    {
        var p = Marshal.StringToHGlobalUni(s);
        _strings.Add(p);
        return p;
    }

    public void Dispose()
    {
        foreach (var p in _strings) Marshal.FreeHGlobal(p);
        if (_buttonsBlock != IntPtr.Zero) Marshal.FreeHGlobal(_buttonsBlock);
    }
}
