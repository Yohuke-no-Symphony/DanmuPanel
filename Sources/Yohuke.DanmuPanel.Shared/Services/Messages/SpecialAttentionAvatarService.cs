using System.Collections.Concurrent;
using System.Globalization;
using Yohuke.DanmuPanel.Models.Messages;

namespace Yohuke.DanmuPanel.Services.Messages;

/// <summary>优先使用搜索结果中的头像，并在不可用时回退到共享用户头像服务。</summary>
public sealed class SpecialAttentionAvatarService(AvatarCacheService images, IUserAvatarService fallback) : IUserAvatarService
{
    private readonly ConcurrentDictionary<long, string> knownAvatars = new();
    private readonly ConcurrentDictionary<long, string> invalidAvatars = new();
    private event Action<long>? knownAvatarChanged;

    /// <inheritdoc />
    public event Action<long>? AvatarChanged
    {
        add
        {
            knownAvatarChanged += value;
            fallback.AvatarChanged += value;
        }
        remove
        {
            knownAvatarChanged -= value;
            fallback.AvatarChanged -= value;
        }
    }

    /// <summary>登记宿主搜索得到的头像地址，并通知关联用户行刷新。</summary>
    public void Register(UserSearchResult result)
    {
        if (result.AvatarUrl.Length == 0 || knownAvatars.TryGetValue(result.UserUid, out var current) && current == result.AvatarUrl)
        {
            return;
        }
        knownAvatars[result.UserUid] = result.AvatarUrl;
        knownAvatarChanged?.Invoke(result.UserUid);
    }

    /// <inheritdoc />
    public async Task<string> GetAvatarAsync(long userUid, CancellationToken cancellationToken = default)
    {
        if (knownAvatars.TryGetValue(userUid, out var url) && invalidAvatars.GetValueOrDefault(userUid) != url)
        {
            var path = await images.DownloadAvatarAsync(userUid.ToString(CultureInfo.InvariantCulture), url, cancellationToken);
            if (path.Length > 0)
            {
                return path;
            }
        }
        return await fallback.GetAvatarAsync(userUid, cancellationToken);
    }

    /// <inheritdoc />
    public Task InvalidateAvatarAsync(long userUid, string invalidPath, CancellationToken cancellationToken = default)
    {
        if (knownAvatars.TryGetValue(userUid, out var url))
        {
            invalidAvatars[userUid] = url;
        }
        return fallback.InvalidateAvatarAsync(userUid, invalidPath, cancellationToken);
    }
}
