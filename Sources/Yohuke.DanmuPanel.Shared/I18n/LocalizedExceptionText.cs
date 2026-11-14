namespace Yohuke.DanmuPanel.I18n;

/// <summary>创建和读取随界面语言变化更新的异常文本。</summary>
public static class LocalizedExceptionText
{
    /// <summary>创建异常并保留本地化信息，使宿主可按当前语言重新显示。</summary>
    public static TException Create<TException>(string key, Func<string, TException> create,
        params object[] arguments) where TException : Exception
    {
        string Render()
        {
            return Localization.Format(key, arguments.Select(argument =>
                argument is Func<string> provider ? provider() : argument).ToArray());
        }

        var error = create(Render());
        error.Data[typeof(LocalizedExceptionText)] = (Func<string>)Render;
        return error;
    }

    /// <summary>按当前界面语言读取异常描述，未附带本地化信息时返回原始消息。</summary>
    public static string Get(Exception error)
    {
        if (error.Data[typeof(LocalizedExceptionText)] is Func<string> provider)
        {
            return provider();
        }

        return CoreExceptionInfo.TryGet(error, out var info) ? CoreErrorText.Get(info) : error.Message;
    }
}
