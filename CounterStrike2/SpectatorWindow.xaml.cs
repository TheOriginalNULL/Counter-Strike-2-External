using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace CounterStrike2
{
    public partial class SpectatorWindow : Window
    {
        public SpectatorWindow()
        {
            InitializeComponent();
            Left = SystemParameters.PrimaryScreenWidth - Width - 20;
            Top  = 60;
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        public void UpdateNames(IReadOnlyList<string> names)
        {
            if (names.Count == 0)
            {
                SpecList.ItemsSource = null;
                EmptyText.Visibility = Visibility.Visible;
                CountBadge.Text      = string.Empty;
            }
            else
            {
                SpecList.ItemsSource = new List<string>(names);
                EmptyText.Visibility = Visibility.Collapsed;
                CountBadge.Text      = $"({names.Count})";
            }
        }
    }
}
