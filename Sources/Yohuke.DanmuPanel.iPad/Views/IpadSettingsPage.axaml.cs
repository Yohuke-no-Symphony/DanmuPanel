using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Views.Dialogs;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>承载触控设置页面并通过系统文件选择器读写分享流。</summary>
public sealed partial class IpadSettingsPage : UserControl, IDisposable
{
    private readonly IpadSettingsViewModel model;
    private readonly IConfirmationService confirmations;

    /// <summary>创建设置草稿与宿主提供的确认交互。</summary>
    public IpadSettingsPage(IpadShellViewModel shell, IConfirmationService confirmations)
    {
        this.confirmations = confirmations;
        AvaloniaXamlLoader.Load(this);
        model = new(shell);
        DataContext = model;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        model.Dispose();
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs args)
    {
        await model.Shell.RunAsync(async () =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null || !provider.CanOpen)
            {
                throw new InvalidOperationException(Localization.Get("iPad.FilePickerUnavailable"));
            }
            var files = await provider.OpenFilePickerAsync(new()
            {
                Title = Localization.Get("Settings.Import"),
                AllowMultiple = false,
                FileTypeFilter = [CreateSettingsFileType(), FilePickerFileTypes.All]
            });
            if (files.Count == 0)
            {
                return;
            }
            using var file = files[0];
            if (!await confirmations.ConfirmAsync(this, Localization.Get("Settings.Import"),
                    Localization.Get("iPad.ImportConfirm")))
            {
                return;
            }
            await using var stream = await file.OpenReadAsync();
            await model.ImportAsync(stream);
        });
    }

    private async void Export_OnClick(object? sender, RoutedEventArgs args)
    {
        await model.Shell.RunAsync(async () =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null || !provider.CanSave)
            {
                throw new InvalidOperationException(Localization.Get("iPad.FilePickerUnavailable"));
            }
            using var file = await provider.SaveFilePickerAsync(new()
            {
                Title = Localization.Get("Settings.Export"),
                SuggestedFileName = "DanmuPanel-settings.json",
                DefaultExtension = "json",
                FileTypeChoices = [CreateSettingsFileType()]
            });
            if (file is null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }
            await model.ExportAsync(stream);
            model.Shell.Notice = Localization.Get("Settings.Exported");
        });
    }

    private static FilePickerFileType CreateSettingsFileType()
    {
        return new(Localization.Get("Settings.FileType"))
        {
            Patterns = ["*.json"],
            MimeTypes = ["application/json"],
            AppleUniformTypeIdentifiers = ["public.json"]
        };
    }
}
