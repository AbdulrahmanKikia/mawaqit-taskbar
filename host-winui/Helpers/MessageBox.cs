using Mawaqit.WinUI.Interop;

namespace Mawaqit.WinUI.Helpers;

internal static class MessageBox
{
    public static MessageBoxResult Info(string message)
        => Show(message, MessageBoxIcon.MB_ICONINFORMATION, MessageBoxButtons.MB_OK);

    public static MessageBoxResult Error(string message)
        => Show(message, MessageBoxIcon.MB_ICONERROR, MessageBoxButtons.MB_OK);

    public static MessageBoxResult Error(string message, MessageBoxButtons buttons)
        => Show(message, MessageBoxIcon.MB_ICONERROR, buttons);

    public static MessageBoxResult Warning(string message)
        => Show(message, MessageBoxIcon.MB_ICONWARNING, MessageBoxButtons.MB_OK);

    private static MessageBoxResult Show(string message, MessageBoxIcon icon, MessageBoxButtons buttons)
    {
        var owner = App.MainHandle;
        uint type = (uint)buttons | (uint)icon;
        return (MessageBoxResult)User32.MessageBox(owner, message, "Mawaqit Widget", type);
    }
}
