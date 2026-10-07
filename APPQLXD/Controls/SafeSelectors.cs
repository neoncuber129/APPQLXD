using System.Collections;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using APPQLXD.Core.Models;

namespace APPQLXD.Controls;

public class SafeComboBox : ComboBox
{
    private int _itemsGuard;

    public SafeComboBox()
    {
        IsSynchronizedWithCurrentItem = false;
        Unloaded += (_, _) => CloseDropDown();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        if (oldParent is not null)
            CloseDropDown();
        base.OnVisualParentChanged(oldParent);
    }

    private void CloseDropDown()
    {
        if (!IsDropDownOpen)
            return;
        try
        {
            IsDropDownOpen = false;
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Đóng danh sách");
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) =>
        SelectorGuard.ChangeItems(this, ref _itemsGuard, ComboBox.TextProperty, e, base.OnItemsChanged);
}

/// <summary>
/// ContentControl giữ lại visual tree theo từng PageVm (instance), tránh tạo lại view mỗi lần đổi tab.
/// </summary>
public class PageHost : ContentControl
{
    private readonly Dictionary<object, FrameworkElement> _views = new();
    private bool _swapping;

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        if (_swapping)
        {
            base.OnContentChanged(oldContent, newContent);
            return;
        }

        DropDownCloser.Close(this);

        if (newContent is null)
        {
            base.OnContentChanged(oldContent, null);
            return;
        }

        // Đã là view (hoặc nội dung UI khác): hiển thị trực tiếp.
        if (newContent is FrameworkElement)
        {
            base.OnContentChanged(oldContent, newContent);
            return;
        }

        if (!_views.TryGetValue(newContent, out var view))
        {
            var template = FindDataTemplate(newContent);
            if (template is null)
            {
                base.OnContentChanged(oldContent, newContent);
                return;
            }

            if (template.LoadContent() is not FrameworkElement created)
            {
                base.OnContentChanged(oldContent, newContent);
                return;
            }

            view = created;
            _views[newContent] = view;
        }

        // Ép DataContextChanged kể cả khi cùng instance — view phiếu nhập/xuất cần
        // gắn lại FormHost sau khi được PageHost đưa trở lại cây visual.
        if (ReferenceEquals(view.DataContext, newContent))
            view.DataContext = null;
        view.DataContext = newContent;
        _swapping = true;
        try
        {
            // Giữ binding SelectedTab.Page; chỉ đổi giá trị hiệu lực sang view đã cache.
            SetCurrentValue(ContentProperty, view);
        }
        finally
        {
            _swapping = false;
        }

        // Layout pass sau khi gắn lại — tránh DataGrid/ContentControl trắng sau cache.
        view.InvalidateMeasure();
        view.InvalidateArrange();
        view.UpdateLayout();
    }

    private DataTemplate? FindDataTemplate(object item)
    {
        var key = new DataTemplateKey(item.GetType());
        for (DependencyObject? walk = this; walk is not null; walk = LogicalTreeHelper.GetParent(walk) ?? VisualTreeHelper.GetParent(walk))
        {
            if (walk is FrameworkElement fe && fe.TryFindResource(key) is DataTemplate local)
                return local;
        }

        return Application.Current?.TryFindResource(key) as DataTemplate;
    }
}

public static class DropDownCloser
{
    public static void Close(DependencyObject? root)
    {
        if (root is null)
            return;
        try
        {
            if (root is ComboBox combo)
                Shut(combo);
            else if (root is DatePicker date && date.IsDropDownOpen)
            {
                try
                {
                    date.IsDropDownOpen = false;
                }
                catch (Exception ex)
                {
                    ErrorLog.Record(ex, "Đóng lịch");
                }
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
                Close(VisualTreeHelper.GetChild(root, i));
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Đóng popup");
        }
    }

    private static void Shut(ComboBox combo)
    {
        if (!combo.IsDropDownOpen)
            return;
        try
        {
            combo.IsDropDownOpen = false;
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Đóng danh sách");
        }
    }
}

public class SafeListBox : ListBox
{
    private int _itemsGuard;

    public SafeListBox() => IsSynchronizedWithCurrentItem = false;

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) =>
        SelectorGuard.ChangeItems(this, ref _itemsGuard, null, e, base.OnItemsChanged);
}

public class SafeDataGrid : DataGrid
{
    private int _itemsGuard;

    public SafeDataGrid() => IsSynchronizedWithCurrentItem = false;

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) =>
        SelectorGuard.ChangeItems(this, ref _itemsGuard, null, e, base.OnItemsChanged);
}

file static class SelectorGuard
{
    public static void ChangeItems(Selector selector, ref int guard, DependencyProperty? textProperty, NotifyCollectionChangedEventArgs e, Action<NotifyCollectionChangedEventArgs> callBase)
    {
        if (!SourceAlive(selector))
            return;

        if (guard > 0)
            return;

        guard++;
        var saved = new List<(DependencyProperty Property, BindingBase Binding)>();
        Hold(selector, Selector.SelectedItemProperty, saved);
        Hold(selector, Selector.SelectedValueProperty, saved);
        Hold(selector, Selector.SelectedIndexProperty, saved);
        if (textProperty is not null)
            Hold(selector, textProperty, saved);
        try
        {
            callBase(e);
        }
        catch (NullReferenceException)
        {
        }
        finally
        {
            if (SourceAlive(selector))
            {
                foreach (var (property, binding) in saved)
                {
                    try
                    {
                        BindingOperations.SetBinding(selector, property, binding);
                    }
                    catch (NullReferenceException)
                    {
                    }
                }
            }

            guard--;
        }
    }

    private static void Hold(Selector selector, DependencyProperty property, List<(DependencyProperty Property, BindingBase Binding)> saved)
    {
        var binding = BindingOperations.GetBindingBase(selector, property);
        if (binding is null)
            return;
        saved.Add((property, binding));
        if (binding is Binding single && single.Mode is BindingMode.TwoWay or BindingMode.Default or BindingMode.OneWayToSource)
        {
            var quiet = new Binding
            {
                Path = single.Path,
                Mode = BindingMode.OneWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
                Converter = single.Converter,
                ConverterParameter = single.ConverterParameter,
                ConverterCulture = single.ConverterCulture,
                StringFormat = single.StringFormat,
                FallbackValue = single.FallbackValue,
                TargetNullValue = single.TargetNullValue
            };
            if (single.Source is not null)
                quiet.Source = single.Source;
            if (single.RelativeSource is not null)
                quiet.RelativeSource = single.RelativeSource;
            if (!string.IsNullOrEmpty(single.ElementName))
                quiet.ElementName = single.ElementName;
            try
            {
                BindingOperations.SetBinding(selector, property, quiet);
                return;
            }
            catch (NullReferenceException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        BindingOperations.ClearBinding(selector, property);
    }

    private static bool SourceAlive(Selector selector)
    {
        var context = selector.DataContext;
        return context is not null && context != BindingOperations.DisconnectedSource;
    }
}

public static class FuelGroupOrder
{
    public static int Rank(string? name) => name switch
    {
        "Dầu" => 0,
        "Xăng" => 1,
        "Nhớt" => 2,
        "Mỡ" => 3,
        _ => 50
    };

    public static string Label(string? name) => string.IsNullOrWhiteSpace(name) ? "Khác" : name.Trim();
}

public sealed class ItemPick
{
    public bool IsHeader { get; init; }
    public string Name { get; init; } = "";
    public ItemRow? Item { get; init; }
}

public class GroupedItemCombo : SafeComboBox
{
    private readonly GroupedPickList _picks;

    public static readonly DependencyProperty SourceItemsProperty = DependencyProperty.Register(
        nameof(SourceItems),
        typeof(IEnumerable),
        typeof(GroupedItemCombo),
        new PropertyMetadata(null, OnSourceItems));

    public IEnumerable? SourceItems
    {
        get => (IEnumerable?)GetValue(SourceItemsProperty);
        set => SetValue(SourceItemsProperty, value);
    }

    public GroupedItemCombo()
    {
        DisplayMemberPath = nameof(ItemPick.Name);
        SelectedValuePath = nameof(ItemPick.Item);
        ItemContainerStyle = GroupedPickList.ItemStyle();
        _picks = new GroupedPickList(this, ItemPicks.Create);
    }

    private static void OnSourceItems(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is GroupedItemCombo combo)
            combo._picks.SetSource(e.NewValue as IEnumerable);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        if (_picks.Depth > 0)
            return;
        if (SelectedItem is ItemPick { IsHeader: true })
        {
            _picks.ClearHeader();
            return;
        }

        base.OnSelectionChanged(e);
    }
}

public sealed class LotPick
{
    public bool IsHeader { get; init; }
    public string Name { get; init; } = "";
    public LotOption? Lot { get; init; }
}

public class GroupedLotCombo : SafeComboBox
{
    private readonly GroupedPickList _picks;

    public static readonly DependencyProperty SourceItemsProperty = DependencyProperty.Register(
        nameof(SourceItems),
        typeof(IEnumerable),
        typeof(GroupedLotCombo),
        new PropertyMetadata(null, OnSourceItems));

    public IEnumerable? SourceItems
    {
        get => (IEnumerable?)GetValue(SourceItemsProperty);
        set => SetValue(SourceItemsProperty, value);
    }

    public GroupedLotCombo()
    {
        DisplayMemberPath = nameof(LotPick.Name);
        SelectedValuePath = nameof(LotPick.Lot);
        ItemContainerStyle = GroupedPickList.ItemStyle();
        _picks = new GroupedPickList(this, LotPicks.Create);
    }

    private static void OnSourceItems(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is GroupedLotCombo combo)
            combo._picks.SetSource(e.NewValue as IEnumerable);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        if (_picks.Depth > 0)
            return;
        if (SelectedItem is LotPick { IsHeader: true })
        {
            _picks.ClearHeader();
            return;
        }

        base.OnSelectionChanged(e);
    }
}

file static class ItemPicks
{
    public static IEnumerable Create(IEnumerable? items)
    {
        var rows = (items ?? Array.Empty<object>()).OfType<ItemRow>()
            .OrderBy(x => FuelGroupOrder.Rank(x.GroupName))
            .ThenBy(x => FuelGroupOrder.Label(x.GroupName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var list = new List<ItemPick>();
        string? current = null;
        foreach (var row in rows)
        {
            var group = FuelGroupOrder.Label(row.GroupName);
            if (current != group)
            {
                list.Add(new ItemPick { IsHeader = true, Name = group });
                current = group;
            }

            list.Add(new ItemPick { Name = row.Name, Item = row });
        }

        return list;
    }
}

file static class LotPicks
{
    public static IEnumerable Create(IEnumerable? items)
    {
        var rows = (items ?? Array.Empty<object>()).OfType<LotOption>()
            .OrderBy(x => FuelGroupOrder.Rank(x.GroupName))
            .ThenBy(x => FuelGroupOrder.Label(x.GroupName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.UnitPrice)
            .ToList();
        var list = new List<LotPick>();
        string? current = null;
        foreach (var row in rows)
        {
            var group = FuelGroupOrder.Label(row.GroupName);
            if (current != group)
            {
                list.Add(new LotPick { IsHeader = true, Name = group });
                current = group;
            }

            list.Add(new LotPick { Name = string.IsNullOrWhiteSpace(row.Display) ? row.ItemName : row.Display, Lot = row });
        }

        return list;
    }
}

sealed class GroupedPickList
{
    private readonly Selector _combo;
    private readonly Func<IEnumerable?, IEnumerable> _create;
    private readonly ObservableCollection<object> _rows = [];
    private IEnumerable? _items;
    private INotifyCollectionChanged? _source;
    private bool _queued;

    public int Depth { get; private set; }

    public GroupedPickList(Selector combo, Func<IEnumerable?, IEnumerable> create)
    {
        _combo = combo;
        _create = create;
        combo.ItemsSource = _rows;
        combo.ItemsPanel = PlainPanel;
        combo.Unloaded += (_, _) => Unhook();
    }

    public void SetSource(IEnumerable? source)
    {
        Unhook();
        _items = source;
        if (source is INotifyCollectionChanged notify)
        {
            _source = notify;
            notify.CollectionChanged += OnChanged;
        }

        Flush();
    }

    public void ClearHeader()
    {
        var header = _combo.SelectedItem switch
        {
            ItemPick pick => pick.IsHeader,
            LotPick pick => pick.IsHeader,
            _ => false
        };
        if (!header)
            return;
        Depth++;
        _combo.SelectedIndex = -1;
        Depth--;
    }

    private static readonly ItemsPanelTemplate PlainPanel = CreatePlainPanel();

    private static ItemsPanelTemplate CreatePlainPanel()
    {
        var template = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        template.Seal();
        return template;
    }

    public static Style ItemStyle()
    {
        var based = Application.Current?.TryFindResource(typeof(ComboBoxItem)) as Style;
        var style = based is null ? new Style(typeof(ComboBoxItem)) : new Style(typeof(ComboBoxItem), based);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 2, 8, 2)));
        var header = new DataTrigger { Binding = new Binding("IsHeader"), Value = true };
        header.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
        header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7A))));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF8))));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 2)));
        style.Triggers.Add(header);
        return style;
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => Queue();

    private void Queue()
    {
        if (_queued)
            return;
        _queued = true;
        _combo.Dispatcher.BeginInvoke(Flush, DispatcherPriority.DataBind);
    }

    private void Flush()
    {
        _queued = false;
        var binding = BindingOperations.GetBindingBase(_combo, Selector.SelectedValueProperty);
        var keep = _combo.SelectedValue;
        if (binding is not null)
            BindingOperations.ClearBinding(_combo, Selector.SelectedValueProperty);
        Depth++;
        _rows.Clear();
        foreach (var row in _create(_items))
            _rows.Add(row);
        if (keep is not null)
            _combo.SelectedValue = keep;
        Depth--;
        if (binding is not null)
            BindingOperations.SetBinding(_combo, Selector.SelectedValueProperty, binding);
        ClearHeader();
    }

    private void Unhook()
    {
        if (_source is null)
            return;
        _source.CollectionChanged -= OnChanged;
        _source = null;
    }
}

public static class SheetColumns
{
    public static DataGridTemplateColumn ItemColumn(IEnumerable items, string header = "Mặt hàng", bool groupByItemGroup = false)
    {
        var display = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Item.Name"));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        display.VisualTree = text;

        var edit = new DataTemplate();
        var combo = new FrameworkElementFactory(groupByItemGroup ? typeof(GroupedItemCombo) : typeof(SafeComboBox));
        if (groupByItemGroup)
        {
            combo.SetValue(GroupedItemCombo.SourceItemsProperty, items);
            combo.SetBinding(Selector.SelectedValueProperty, new Binding("Item") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        }
        else
        {
            combo.SetValue(ItemsControl.ItemsSourceProperty, items);
            combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Name");
            combo.SetBinding(Selector.SelectedItemProperty, new Binding("Item") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        }

        combo.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        combo.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        combo.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        edit.VisualTree = combo;

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = 240,
            CellTemplate = display,
            CellEditingTemplate = edit
        };
    }

    public static DataGridTemplateColumn LotTypeColumn(IEnumerable lotTypes, string header = "Loại lô")
    {
        var display = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("LotTypeCode"));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        });
        display.VisualTree = text;

        var edit = new DataTemplate();
        var combo = new FrameworkElementFactory(typeof(SafeComboBox));
        combo.SetValue(ItemsControl.ItemsSourceProperty, lotTypes);
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Code");
        combo.SetValue(ComboBox.IsEditableProperty, false);
        combo.SetValue(ComboBox.IsTextSearchEnabledProperty, false);
        combo.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        combo.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        combo.SetBinding(Selector.SelectedItemProperty, new Binding("LotType") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        combo.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        combo.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        combo.SetBinding(UIElement.IsEnabledProperty, new Binding("LotTypeLocked") { Converter = new InverseBoolConverter() });
        edit.VisualTree = combo;

        var column = new DataGridTemplateColumn
        {
            Header = header,
            Width = 88,
            CellTemplate = display,
            CellEditingTemplate = edit
        };
        var style = new Style(typeof(DataGridCell));
        var locked = new DataTrigger { Binding = new Binding("LotTypeLocked"), Value = true };
        locked.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        var total = new DataTrigger { Binding = new Binding("IsGroupTotal"), Value = true };
        total.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        var sheetHeader = new DataTrigger { Binding = new Binding("IsSheetHeader"), Value = true };
        sheetHeader.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        style.Triggers.Add(locked);
        style.Triggers.Add(total);
        style.Triggers.Add(sheetHeader);
        column.CellStyle = style;
        return column;
    }

    public static DataGridTemplateColumn LotOriginColumn(IEnumerable lotOrigins, string header = "Nguồn gốc")
    {
        var display = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("OriginName"));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        });
        display.VisualTree = text;

        var edit = new DataTemplate();
        var combo = new FrameworkElementFactory(typeof(SafeComboBox));
        combo.SetValue(ItemsControl.ItemsSourceProperty, lotOrigins);
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Name");
        combo.SetValue(ComboBox.IsEditableProperty, false);
        combo.SetValue(ComboBox.IsTextSearchEnabledProperty, false);
        combo.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        combo.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        combo.SetBinding(Selector.SelectedItemProperty, new Binding("OriginOption") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        combo.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        combo.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        combo.SetBinding(UIElement.IsEnabledProperty, new Binding("OriginLocked") { Converter = new InverseBoolConverter() });
        edit.VisualTree = combo;

        var column = new DataGridTemplateColumn
        {
            Header = header,
            Width = 96,
            CellTemplate = display,
            CellEditingTemplate = edit
        };
        var style = new Style(typeof(DataGridCell));
        var locked = new DataTrigger { Binding = new Binding("OriginLocked"), Value = true };
        locked.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        var total = new DataTrigger { Binding = new Binding("IsGroupTotal"), Value = true };
        total.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        var sheetHeader = new DataTrigger { Binding = new Binding("IsSheetHeader"), Value = true };
        sheetHeader.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        style.Triggers.Add(locked);
        style.Triggers.Add(total);
        style.Triggers.Add(sheetHeader);
        column.CellStyle = style;
        return column;
    }

    public static DataGridTemplateColumn OpeningItemColumn(IEnumerable items, string header = "Mặt hàng")
    {
        var display = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("ItemDisplayName"));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        // Kế thừa màu chữ từ hàng (header nhóm/loại lô = trắng).
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        });
        var centerWhenHeader = new Style(typeof(TextBlock));
        var headerAlign = new DataTrigger { Binding = new Binding("IsSheetHeader"), Value = true };
        headerAlign.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        headerAlign.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        headerAlign.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0)));
        centerWhenHeader.Triggers.Add(headerAlign);
        text.SetValue(FrameworkElement.StyleProperty, centerWhenHeader);
        display.VisualTree = text;

        var edit = new DataTemplate();
        var combo = new FrameworkElementFactory(typeof(GroupedItemCombo));
        combo.SetValue(GroupedItemCombo.SourceItemsProperty, items);
        combo.SetBinding(Selector.SelectedValueProperty, new Binding("Item") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        combo.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        combo.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        combo.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        edit.VisualTree = combo;

        var column = new DataGridTemplateColumn
        {
            Header = header,
            Width = 240,
            CellTemplate = display,
            CellEditingTemplate = edit
        };
        var style = new Style(typeof(DataGridCell));
        var sheetHeader = new DataTrigger { Binding = new Binding("IsSheetHeader"), Value = true };
        sheetHeader.Setters.Add(new Setter(DataGridCell.IsEnabledProperty, false));
        style.Triggers.Add(sheetHeader);
        column.CellStyle = style;
        return column;
    }

    private sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
