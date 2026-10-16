using System;
using System.Windows.Data;
using System.Windows.Markup;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Localization;

/// <summary>
/// XAML shortcut for a translated string: <c>Text="{l:T Tab.Rules}"</c>.
/// </summary>
/// <remarks>
/// It produces a binding, not a plain string, so switching language refreshes every label of the
/// window without recreating it: the service raises <c>PropertyChanged("Item[]")</c> and WPF
/// re-reads every indexer binding.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    /// <summary>For the property form, <c>{l:T Key=Tab.Rules}</c>.</summary>
    public TExtension()
    {
    }

    /// <summary>For the short form, <c>{l:T Tab.Rules}</c>.</summary>
    public TExtension(string key) => Key = key;

    /// <summary>The translation key, such as <c>Tab.Rules</c>.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    /// <summary>A one way binding to the key on the localisation service, so it follows language changes.</summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        Binding binding = new($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
