using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

public partial class NearbyDiscoveryPage : ContentPage
{
    public NearbyDiscoveryPage(NearbyDiscoveryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
