using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Player.App.Controls;

/// <summary>Polite feedback for code-built dialogs, using the same UIA event as the main window.</summary>
internal static class AccessibleStatus
{
    internal static TextBlock Create()
    {
        var text = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return text;
    }

    internal static void Update(TextBlock text, string message)
    {
        if (text.Text == message) return;
        text.Text = message;
        if (!text.IsLoaded || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        text.Dispatcher.BeginInvoke(() =>
        {
            if (text.IsLoaded && text.Text == message)
                UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Background);
    }
}
