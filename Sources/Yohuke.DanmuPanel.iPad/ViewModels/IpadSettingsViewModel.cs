using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Models.Translation;
using Yohuke.DanmuPanel.Services.Settings;
using Yohuke.DanmuPanel.ViewModels.Settings;

namespace Yohuke.DanmuPanel.iPad.ViewModels;

/// <summary>组合共享设置草稿与 iPad 偏好，并通过共享验证和保存流程应用修改。</summary>
public sealed partial class IpadSettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsShareService sharing = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private bool disposed;
    private PanelSettings draft;
    [ObservableProperty] private SettingsAppearanceViewModel appearance;
    [ObservableProperty] private SettingsReadingViewModel reading;
    [ObservableProperty] private SettingsTranslationViewModel translation;
    [ObservableProperty] private bool keepScreenOn;

    public IpadShellViewModel Shell { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>建立独立草稿，页面编辑不会提前改变运行中的设置。</summary>
    public IpadSettingsViewModel(IpadShellViewModel shell)
    {
        Shell = shell;
        lifetimeToken = lifetime.Token;
        draft = sharing.Snapshot(shell.Runtime.Configuration.Settings);
        appearance = new(draft, false);
        reading = new(draft);
        translation = new(draft.Translation);
        keepScreenOn = shell.KeepScreenOn;
        SaveCommand = new AsyncRelayCommand(() => shell.RunAsync(SaveAsync));
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>验证全部草稿并保存设置；失败时恢复当前内存设置和运行服务。</summary>
    public async Task SaveAsync()
    {
        if (disposed)
        {
            return;
        }
        if (Reading.MessageRetentionCount is not { } retention || decimal.Truncate(retention) != retention ||
            Reading.MessageTextScale is not { } scale || Reading.GiftMinimum is not { } minimum ||
            !Appearance.TryCreateBorders(out var borders))
        {
            throw new InvalidOperationException(Localization.Get("iPad.InvalidNumericSettings"));
        }
        draft.Language = SettingsAppearanceViewModel.LanguageIds[Appearance.LanguageIndex];
        draft.Theme = SettingsAppearanceViewModel.ThemeIds[Appearance.ThemeIndex];
        draft.MessageBorders = borders;
        draft.MessageRetentionCount = checked((int)retention);
        draft.MessageTextScale = (double)scale / 100;
        draft.Reading = new()
        {
            GiftAutoPinMinimum = minimum,
            GiftReadResetPolicy = (GiftReadResetPolicy)Reading.GiftResetPolicy
        };
        draft.ShowUserAvatars = Reading.ShowUserAvatars;
        draft.ShowFanMedals = Reading.ShowFanMedals;
        draft.HideEmoticonDanmu = Reading.HideEmoticonDanmu;
        draft.CompactDanmuDisplay = Reading.CompactDisplay;
        draft.CompactDanmuWrap = Reading.CompactDanmuWrap;
        draft.CompactSuperChatWrap = Reading.CompactSuperChatWrap;
        draft.AutoScrollLongMessages = Reading.AutoScrollLongMessages;
        draft.ShowUserPinyin = Reading.UserPinyin;
        draft.ShowGiftPinyin = Reading.GiftPinyin;
        draft.ShowDanmuPinyin = Reading.DanmuPinyin;
        draft.ShowSuperChatPinyin = Reading.SuperChatPinyin;
        draft.PinyinLayout = (PinyinLayoutMode)Reading.PinyinLayout;
        draft.MessageSorting = Reading.CreateSorting();
        draft.Translation.Enabled = Translation.Enabled;
        draft.Translation.BaseUrl = Translation.SelectedBaseUrl;
        draft.Translation.ApiKey = Translation.ApiKey?.Trim() ?? "";
        draft.Translation.Model = Translation.Model?.Trim() ?? "";
        draft.Translation.TargetLanguage = Translation.SelectedLanguage.Code;
        draft.Translation.ExtraSystemPrompt = Translation.ExtraPrompt ?? "";
        draft.Translation.DisplayMode = (TranslationDisplayMode)Translation.DisplayMode;
        draft.Translation.BacklogMode = (TranslationBacklogMode)Translation.BacklogMode;
        draft.Translation.Glossary = Translation.Glossary.SelectMany(row => row.GetEntries()).ToList();
        try
        {
            await ApplyAsync(sharing.Snapshot(draft));
            if (!disposed)
            {
                Shell.Notice = Localization.Get("iPad.SettingsSaved");
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>导入已经由共享服务验证的设置，并重建页面草稿。</summary>
    public async Task ImportAsync(Stream source)
    {
        try
        {
            var imported = await sharing.ImportAsync(source, lifetimeToken);
            await ApplyAsync(imported);
            if (!disposed)
            {
                Reload();
                Shell.Notice = Localization.Get("iPad.SettingsImported");
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>导出已保存的全局设置，保留共享格式及 API 密钥。</summary>
    public Task ExportAsync(Stream destination)
    {
        return sharing.ExportAsync(Shell.Runtime.Configuration.Settings, destination, lifetimeToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private Task ApplyAsync(PanelSettings settings)
    {
        var screenOn = KeepScreenOn;
        return Shell.ExecuteSettingsUpdateAsync(async () =>
        {
            var current = Shell.Runtime.Configuration.Settings;
            var previous = sharing.Snapshot(current);
            var previousKeepOn = Shell.KeepScreenOn;
            sharing.ApplyTo(current, settings);
            try
            {
                await Shell.Runtime.ApplySettingsAsync();
                await Shell.ApplyPreferencesAsync(screenOn);
            }
            catch
            {
                sharing.RestoreTo(current, previous);
                await Shell.Runtime.ApplySettingsAsync();
                await Shell.ApplyPreferencesAsync(previousKeepOn);
                throw;
            }
        }, lifetimeToken);
    }

    private void Reload()
    {
        draft = sharing.Snapshot(Shell.Runtime.Configuration.Settings);
        Appearance = new(draft, false);
        Reading = new(draft);
        Translation = new(draft.Translation);
        KeepScreenOn = Shell.KeepScreenOn;
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        Appearance.RefreshLocalization();
        Reading.RefreshLocalization();
        Translation.RefreshLocalization();
    }
}
