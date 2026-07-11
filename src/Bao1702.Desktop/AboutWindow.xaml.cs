using System.Reflection;
using System.Windows;

namespace Bao1702.Desktop;

/// <summary>About dialog displaying version, copyright, and license information.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version unknown";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
}
