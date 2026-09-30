using NetworkMapViewerV2.Models;
using System.Windows;
// Add using for your ExternalCommand namespace here

namespace NetworkMapViewerV2.Views
{
    public partial class CommandEditorWindow : Window
    {
        private readonly ExternalCommand _cmd;

        public CommandEditorWindow(ExternalCommand cmd, string title)
        {
            InitializeComponent();
            Title = title;
            _cmd = cmd;

            TxtName.Text = cmd.Name;
            TxtIcon.Text = cmd.Icon;
            TxtExe.Text = cmd.Path;
            TxtArgs.Text = cmd.Arguments;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _cmd.Name = TxtName.Text.Trim();
            _cmd.Icon = TxtIcon.Text.Trim();
            _cmd.Path = TxtExe.Text.Trim();
            _cmd.Arguments = TxtArgs.Text.Trim();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}