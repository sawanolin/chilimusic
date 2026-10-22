using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal static class ReferenceLayout
{
    internal static void GridViewCorner(ListView list)
    {
        // The built-in GridView template paints two literal white highlights in the scrollbar corner.
        foreach (var corner in UiVerification.FindAll<DockPanel>(list).Where(p => Grid.GetRow(p) == 1 && Grid.GetColumn(p) == 1 && p.Children.OfType<System.Windows.Shapes.Rectangle>().Count() == 2))
        {
            corner.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "SurfaceBrush");
            foreach (var edge in corner.Children.OfType<System.Windows.Shapes.Rectangle>()) edge.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SurfaceBrush");
        }
    }
    internal static void Tabs(System.Windows.Controls.TabControl tabs)
    {
        var item = new Style(typeof(TabItem)); item.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new DynamicResourceExtension("MutedBrush"))); item.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(14, 8, 14, 9)));
        var border = new FrameworkElementFactory(typeof(Border), "Tab"); border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        var label = new FrameworkElementFactory(typeof(ContentPresenter)); label.SetValue(ContentPresenter.ContentSourceProperty, "Header"); border.AppendChild(label); var itemTemplate = new ControlTemplate(typeof(TabItem)) { VisualTree = border }; var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Tab")); selected.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new DynamicResourceExtension("AccentBrush"))); itemTemplate.Triggers.Add(selected); item.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, itemTemplate)); tabs.ItemContainerStyle = item;
        tabs.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TabControl"><Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions><Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1"><TabPanel IsItemsHost="True"/></Border><ContentPresenter x:Name="PART_SelectedContentHost" Grid.Row="1" ContentSource="SelectedContent" Margin="0,10,0,0" TextElement.Foreground="{DynamicResource TextBrush}"/></Grid></ControlTemplate>
""");
    }
}
