using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is SettingsViewModel vm)
        {
            vm.InitializeCommand.Execute(null);
        }
    }

    private void OnDisplayNameCompleted(object sender, EventArgs e)
    {
        if (BindingContext is SettingsViewModel vm)
        {
            vm.SaveDisplayNameCommand.Execute(null);
        }
    }
}
