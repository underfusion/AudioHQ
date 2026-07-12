using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AudioHQ.App;

public static class RenameTextBoxController
{
    public static void FocusWhenVisible(TextBox box)
    {
        if (!box.IsVisible) return;
        box.Dispatcher.BeginInvoke(new Action(() =>
        {
            box.Focus();
            box.SelectAll();
        }), DispatcherPriority.Input);
    }

    public static bool HandleKeyDown(TextBox box, KeyEventArgs e, Action closeEditor)
    {
        if (e.Key == Key.Enter)
        {
            Commit(box, closeEditor);
            return true;
        }

        if (e.Key != Key.Escape) return false;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        closeEditor();
        return true;
    }

    public static void Commit(TextBox box, Action closeEditor)
    {
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        closeEditor();
    }
}
