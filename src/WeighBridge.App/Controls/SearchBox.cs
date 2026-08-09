using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WeighBridge.App.Controls;

/// <summary>
/// Text input with a search glyph, placeholder text and a clear button.
/// </summary>
/// <remarks>
/// <see cref="SearchText"/> is two-way by default so a ViewModel can bind it directly
/// and filter as the operator types; <see cref="SearchCommand"/> fires on Enter for
/// searches that are too expensive to run on every keystroke.
/// </remarks>
[TemplatePart(Name = TextBoxPartName, Type = typeof(TextBox))]
[TemplatePart(Name = ClearButtonPartName, Type = typeof(ButtonBase))]
public sealed class SearchBox : Control
{
    private const string TextBoxPartName = "PART_TextBox";
    private const string ClearButtonPartName = "PART_ClearButton";

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(SearchBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SearchBox),
            new PropertyMetadata("Search"));

    public static readonly DependencyProperty SearchCommandProperty =
        DependencyProperty.Register(
            nameof(SearchCommand),
            typeof(ICommand),
            typeof(SearchBox),
            new PropertyMetadata(null));

    private TextBox? _textBox;
    private ButtonBase? _clearButton;

    static SearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SearchBox),
            new FrameworkPropertyMetadata(typeof(SearchBox)));
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Templates can be reapplied (theme switch), so always detach the old handler.
        if (_textBox is not null)
        {
            _textBox.KeyDown -= OnTextBoxKeyDown;
        }

        if (_clearButton is not null)
        {
            _clearButton.Click -= OnClearClicked;
        }

        _textBox = GetTemplateChild(TextBoxPartName) as TextBox;
        _clearButton = GetTemplateChild(ClearButtonPartName) as ButtonBase;

        if (_textBox is not null)
        {
            _textBox.KeyDown += OnTextBoxKeyDown;
        }

        if (_clearButton is not null)
        {
            _clearButton.Click += OnClearClicked;
        }
    }

    /// <summary>Moves keyboard focus into the inner text box.</summary>
    public void FocusInput() => _textBox?.Focus();

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        SearchText = string.Empty;

        // Clearing without returning focus leaves the caret nowhere, which reads as
        // the control having been dismissed rather than emptied.
        _textBox?.Focus();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SearchCommand?.CanExecute(SearchText) == true:
                SearchCommand.Execute(SearchText);
                e.Handled = true;
                break;

            case Key.Escape when !string.IsNullOrEmpty(SearchText):
                SearchText = string.Empty;
                e.Handled = true;
                break;
        }
    }
}
