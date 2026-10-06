from pathlib import Path

p = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\Views\VoucherDeskView.xaml")
text = p.read_text(encoding="utf-8")
start = text.find('            <Border Grid.Row="2" Margin="0,0,0,8" Padding="12"')
end = text.find('            <ScrollViewer Grid.Row="3"')
print("start", start, "end", end)
if start < 0 or end < 0 or start > end:
    raise SystemExit(1)
text = text[:start] + text[end:].replace('            <ScrollViewer Grid.Row="3"', '            <ScrollViewer Grid.Row="2"', 1)
old = """            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>"""
new = """            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>"""
count = text.count(old)
print("rowdefs", count)
if count != 1:
    raise SystemExit(2)
text = text.replace(old, new, 1)
p.write_text(text, encoding="utf-8")
print("ok")
