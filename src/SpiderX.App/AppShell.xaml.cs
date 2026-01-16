using SpiderX.App.Views;

namespace SpiderX.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes
        Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
    }
}
