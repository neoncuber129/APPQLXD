using System.Collections;
using System.Windows;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class OpeningLotWindow : Window
{
    public OpeningLotWindow(IEnumerable items, IEnumerable lotTypes)
    {
        InitializeComponent();
        ItemCombo.SourceItems = items;
        LotTypeCombo.ItemsSource = lotTypes;
        LotTypeCombo.SelectedItem = lotTypes.OfType<LotTypeRow>().FirstOrDefault(x => x.Id == SeedIds.LotTypeTx)
                                    ?? lotTypes.OfType<LotTypeRow>().FirstOrDefault();
        PriceBox.Text = "0";
    }

    public ItemRow? SelectedItem { get; private set; }
    public string PriceText { get; private set; } = "0";
    public LotTypeRow? SelectedLotType { get; private set; }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        SelectedItem = ItemCombo.SelectedValue as ItemRow;
        if (SelectedItem is null)
        {
            ErrorText.Text = "Chọn mặt hàng.";
            return;
        }

        PriceText = PriceBox.Text?.Trim() ?? "";
        if (!Numbers.Try(PriceText, out var price) || !QuantityMath.TryWholeMoney(price, out _) || price < 0)
        {
            ErrorText.Text = "Đơn giá phải là số nguyên không âm.";
            return;
        }

        SelectedLotType = LotTypeCombo.SelectedItem as LotTypeRow;
        if (SelectedLotType is null)
        {
            ErrorText.Text = "Chọn loại lô.";
            return;
        }

        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
