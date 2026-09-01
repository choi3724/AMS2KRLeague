using System.Windows;

namespace AMS2LeagueClient.Presentation
{
    public partial class ClientStatusWindow : Window
    {
        public ClientStatusWindow(ClientStatusViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        public ClientStatusViewModel ViewModel { get; }
    }
}
