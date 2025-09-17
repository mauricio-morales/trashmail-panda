using Avalonia.Controls;
using TrashMailPanda.ViewModels;

namespace TrashMailPanda.Views;

public partial class GoogleOAuthSetupDialog : Window
{
    public GoogleOAuthSetupDialog()
    {
        InitializeComponent();
    }

    public GoogleOAuthSetupDialog(GoogleOAuthSetupViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Subscribe to close request
        viewModel.RequestClose += (_, _) => Close(viewModel.DialogResult);
    }
}