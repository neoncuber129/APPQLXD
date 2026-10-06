using System.Windows.Controls;
using System.Windows.Input;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class SamplesView : UserControl
{
    public SamplesView() => InitializeComponent();

    private void DraftKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || box.DataContext is not SampleFieldCardVm card)
            return;
        if (DataContext is SamplesVm vm)
            vm.AddDraftCommand.Execute(card);
        e.Handled = true;
    }
}