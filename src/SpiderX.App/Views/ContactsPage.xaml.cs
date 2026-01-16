using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

public partial class ContactsPage : ContentPage
{
    public ContactsPage(ContactsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is ContactsViewModel vm)
        {
            await vm.InitializeCommand.ExecuteAsync(null);
        }
    }
}
