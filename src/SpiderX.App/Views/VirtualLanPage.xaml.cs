using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

public partial class VirtualLanPage : ContentPage
{
    public VirtualLanPage(VirtualLanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
