// Copyright (c) Pixeval.
// Licensed under the GPL v3 License.

using System;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Pixeval.AppManagement;
using Pixeval.Attributes;
using Pixeval.Controls.Settings;
using Pixeval.Options;
using Windows.System;
using Symbol = FluentIcons.Common.Symbol;

namespace Pixeval.Settings.Models;

public partial class ProxyAppSettingsEntry : EnumAppSettingsEntry
{
    public ProxyAppSettingsEntry(AppSettings settings) : base(settings, t => t.ProxyType, ProxyTypeExtension.GetItems())
    {
        var member = typeof(AppSettings).GetProperty(nameof(AppSettings.Proxy));
        Attribute2 = member?.GetCustomAttribute<SettingsEntryAttribute>();

        if (Attribute2 is { } attribute)
        {
            Header2 = attribute.LocalizedResourceHeader;
            Description2 = attribute.LocalizedResourceDescription;
            HeaderIcon2 = attribute.Symbol;
        }
    }

    #region Entry2

    public Symbol HeaderIcon2 { get; set; }

    public string Header2 { get; set; } = "";

    public object DescriptionControl2
    {
        get
        {
            if (DescriptionUri2 is not null)
            {
                var b = new HyperlinkButton { Content = Description2 };
                if (DescriptionUri2.Scheme is "http" or "https")
                {
                    b.NavigateUri = DescriptionUri2;
                    return b;
                }

                var uri = DescriptionUri2;
                b.Click += (_, _) => _ = Launcher.LaunchUriAsync(uri);
                return b;
            }
            return Description2;
        }
    }

    public string Description2 { get; set; } = "";

    public Uri? DescriptionUri2 { get; set; }

    public SettingsEntryAttribute? Attribute2 { get; }

    #endregion

    public override ProxySettingsExpander Element => new() { Entry = this };

    public Action<string?>? ProxyChanged { get; set; }

    public string? Proxy
    {
        get => Settings.Proxy;
        set
        {
            if (Settings.Proxy == value)
                return;
            Settings.Proxy = value ?? "";
            OnPropertyChanged();
            ProxyChanged?.Invoke(MakoProxy);
        }
    }

    private string? MakoProxy
    {
        get
        {
            string scheme;
            switch (Value)
            {
                case ProxyType.System:
                    return "";
                case ProxyType.Http:
                    scheme = "http";
                    break;
                case ProxyType.Socks4:
                    scheme = "socks4";
                    break;
                case ProxyType.Socks4A:
                    scheme = "socks4a";
                    break;
                case ProxyType.Socks5:
                    scheme = "socks5";
                    break;
                default:
                    return null;
            }

            if (!Uri.TryCreate(Proxy, UriKind.Absolute, out var uri))
                return null;
            var builder = new UriBuilder(uri) { Scheme = scheme };
            return builder.ToString();
        }
    }

    public override void ValueReset(AppSettings defaultSettings)
    {
        base.ValueReset(defaultSettings);
        Proxy = defaultSettings.Proxy;
    }
}
