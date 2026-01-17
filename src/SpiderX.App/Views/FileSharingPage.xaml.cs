using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

public partial class FileSharingPage : ContentPage
{
    public FileSharingPage(FileSharingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
