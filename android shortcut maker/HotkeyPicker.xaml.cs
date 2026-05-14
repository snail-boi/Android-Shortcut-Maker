using System.Windows;
using System.Windows.Input;

namespace android_shortcut_maker;

public partial class HotkeyPicker : Window
{
    /// <summary>The recorded virtual key code, 0 if cleared or cancelled.</summary>
    public int VirtualKey { get; private set; }

    public HotkeyPicker()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses.
        if (key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin)
            return;

        VirtualKey = KeyInterop.VirtualKeyFromKey(key) & 0xFF;
        TxtKey.Text = VkHelper.ToDisplayName(VirtualKey);
        BtnOk.IsEnabled = true;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        VirtualKey = 0;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
