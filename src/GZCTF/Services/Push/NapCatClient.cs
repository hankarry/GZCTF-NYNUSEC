using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Request.Edit;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Services.Push;

// 用于表示向 NapCat HTTP API 发送群消息的请求
public sealed record NapCatSendGroupRequest(
    [property: JsonPropertyName("group_id")] string GroupId,
    [property: JsonPropertyName("message")] NapCatTextSegment[] Message);

// 用于表示 NapCat 消息中的文本段
public sealed record NapCatTextSegment(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] NapCatTextData Data);

// 用于表示 NapCat 消息中的文本数据
public sealed record NapCatTextData([property: JsonPropertyName("text")] string Text);

// 用于表示 NapCat 消息发送失败的情况
public sealed class NapCatDeliveryException(string message, bool retryable = true) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

public class NapCatClient(HttpClient httpClient, IDataProtectionProvider protectionProvider)
{
    internal const string TokenPurpose = "GZCTF.NapCat.AccessToken.v1";

    // 异步发送消息到 NapCat HTTP API
    public async Task SendAsync(GamePushConfig config, string message, CancellationToken token = default)
    {
        if (!GamePushSettingsModel.IsValidEndpoint(config.EndpointUrl) ||
            !GamePushSettingsModel.IsValidGroupId(config.GroupId))
            throw new NapCatDeliveryException("The saved NapCat endpoint or QQ group ID is invalid.", false);

        var endpoint = new Uri(new Uri(config.EndpointUrl.TrimEnd('/') + "/"), "send_group_msg");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrEmpty(config.ProtectedAccessToken))
        {
            try
            {
                // 解密保存的访问令牌以便在请求中使用
                var accessToken = protectionProvider.CreateProtector(TokenPurpose).Unprotect(config.ProtectedAccessToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new NapCatDeliveryException("The saved token cannot be decrypted. Save the access token again.", false);
            }
        }
        // Text segments prevent team names and announcements from being interpreted as CQ commands.
        // 文本段可以防止队伍名和公告被解析为 CQ 命令。
        request.Content = JsonContent.Create(
            new NapCatSendGroupRequest(config.GroupId, [new("text", new(message))]),
            AppJsonSerializerContext.Default.NapCatSendGroupRequest);
        try
        {
            using var response = await httpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new NapCatDeliveryException($"NapCat returned HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode is 408 or 429 or >= 500);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token),
                cancellationToken: token);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "ok" ||
                !root.TryGetProperty("retcode", out var code) || code.ValueKind != JsonValueKind.Number ||
                !code.TryGetInt32(out var retcode) || retcode != 0)
                throw new NapCatDeliveryException("NapCat did not confirm successful delivery (status/retcode).");
        }
        catch (HttpRequestException)
        {
            throw new NapCatDeliveryException("Could not connect to the NapCat HTTP API.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new NapCatDeliveryException("The NapCat HTTP request timed out.");
        }
        catch (JsonException)
        {
            throw new NapCatDeliveryException("NapCat returned an invalid JSON response.");
        }
    }
}
