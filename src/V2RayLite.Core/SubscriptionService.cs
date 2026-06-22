namespace V2RayLite.Core;

public sealed class SubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly SubscriptionParser _parser;
    private readonly SettingsStore _store;

    public SubscriptionService(HttpClient httpClient, SubscriptionParser parser, SettingsStore store)
    {
        _httpClient = httpClient;
        _parser = parser;
        _store = store;
    }

    public async Task<SubscriptionSnapshot> UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SubscriptionUrl))
        {
            return new SubscriptionSnapshot { StatusText = "订阅地址为空" };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, settings.SubscriptionUrl);
            request.Headers.UserAgent.ParseAdd("V2RayLite/1.0");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var nodes = _parser.Parse(payload).ToList();
            if (nodes.Count == 0)
            {
                return new SubscriptionSnapshot
                {
                    Nodes = await _store.LoadNodesAsync(cancellationToken),
                    LastUpdated = settings.LastSubscriptionUpdate,
                    StatusText = "未解析到有效节点，已保留原节点"
                };
            }

            await _store.SaveNodesAsync(nodes, cancellationToken);
            var previousUpdate = settings.LastSubscriptionUpdate;
            settings.LastSubscriptionUpdate = DateTimeOffset.Now;
            try
            {
                await _store.SaveSettingsAsync(settings, cancellationToken);
            }
            catch
            {
                settings.LastSubscriptionUpdate = previousUpdate;
                throw;
            }

            return new SubscriptionSnapshot
            {
                Nodes = nodes,
                LastUpdated = settings.LastSubscriptionUpdate,
                StatusText = nodes.Count > 0 ? "正常" : "订阅为空"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SubscriptionSnapshot
            {
                Nodes = await _store.LoadNodesAsync(cancellationToken),
                LastUpdated = settings.LastSubscriptionUpdate,
                StatusText = $"更新失败：{ex.Message}"
            };
        }
    }
}
