using Avalonia.Controls;
using Avalonia.Input;

namespace File.Commander.Presentation.Views.Network;

/// <summary>The Network page. DataContext: <see cref="NetworkViewModel"/>.</summary>
public partial class NetworkView : UserControl
{
    public NetworkView() => InitializeComponent();

    /// <summary>Enter in Connect to server connects, like the button.</summary>
    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || DataContext is not NetworkViewModel vm)
            return;

        if (vm.ConnectCommand.CanExecute(null))
            vm.ConnectCommand.Execute(null);

        e.Handled = true;
    }
}
