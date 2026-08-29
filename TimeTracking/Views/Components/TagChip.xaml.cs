using System.Windows;
using System.Windows.Controls;
using TimeTracking.Models;

namespace TimeTracking.Views.Components;

public partial class TagChip : UserControl
{
    public static readonly DependencyProperty TagItemProperty =
        DependencyProperty.Register(nameof(TagItem), typeof(Tag), typeof(TagChip));

    public Tag? TagItem
    {
        get => (Tag?)GetValue(TagItemProperty);
        set => SetValue(TagItemProperty, value);
    }

    public TagChip()
    {
        InitializeComponent();
    }
}
