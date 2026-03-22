using System.Windows;

namespace android_shortcut_maker;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public InputDialog(string title, string prompt, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        TxtInput.Text = defaultValue;
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        InputText = TxtInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
