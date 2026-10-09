using System.Diagnostics;
using System.Text.Json;

namespace AiBurgerClock;

// Explicit Windows smoke only: no real account data, UI input, sleeps or policy changes.
internal sealed class SmokeQueryActivity
{
    private sealed class Activity
    {
        internal int Calls;
        internal int DelayMs;
        internal double? LastDurationMs;
        internal readonly Dictionary<int, double> Started = new();
    }

    private readonly object sync = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Dictionary<string, Activity> activities = new();
    private int nextId;
    internal double ElapsedMs => elapsed.Elapsed.TotalMilliseconds;

    internal IDisposable Begin(string name, int delayMs = 0)
    {
        int id;
        double started = ElapsedMs;
        lock (sync)
        {
            if (!activities.TryGetValue(name, out var activity)) activities[name] = activity = new();
            id = ++nextId;
            activity.Calls++;
            activity.DelayMs = delayMs;
            activity.Started.Add(id, started);
        }
        Write("query.begin", new { name, id, delayMs });
        return new QueryScope(() =>
        {
            double duration = ElapsedMs - started;
            lock (sync)
            {
                activities[name].Started.Remove(id);
                activities[name].LastDurationMs = duration;
            }
            Write("query.end", new { name, id, durationMs = duration });
        });
    }

    // Monitor duration is measured from its observed Changed event; fake reads are exact.
    internal void ObserveRefreshing(string name, bool refreshing)
    {
        lock (sync)
        {
            if (!activities.TryGetValue(name, out var activity)) activities[name] = activity = new();
            if (refreshing && !activity.Started.ContainsKey(0))
            {
                activity.Calls++;
                activity.Started[0] = ElapsedMs;
            }
            else if (!refreshing && activity.Started.Remove(0, out double started))
                activity.LastDurationMs = ElapsedMs - started;
        }
    }

    internal object[] Snapshot()
    {
        lock (sync)
        {
            double now = ElapsedMs;
            return activities.OrderBy(pair => pair.Key).Select(pair => (object)new
            {
                name = pair.Key,
                calls = pair.Value.Calls,
                active = pair.Value.Started.Count,
                activeElapsedMs = pair.Value.Started.Values.Select(started => now - started).ToArray(),
                delayMs = pair.Value.DelayMs,
                lastDurationMs = pair.Value.LastDurationMs
            }).ToArray();
        }
    }

    internal void Write(string point, object state)
    {
        try
        {
            Console.Error.WriteLine("DIAG: " + JsonSerializer.Serialize(new
            {
                elapsedMs = ElapsedMs,
                thread = Environment.CurrentManagedThreadId,
                point,
                state
            }));
        }
        catch (Exception) { /* Diagnostics must never replace the original test failure or prevent exit. */ }
    }

    private sealed class QueryScope(Action finish) : IDisposable
    {
        private Action? finish = finish;
        public void Dispose() => Interlocked.Exchange(ref finish, null)?.Invoke();
    }
}

internal sealed class SmokeStatusHttpHandler(SmokeQueryActivity activity, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string host = request.RequestUri!.Host;
        string provider = host.Contains("openai", StringComparison.Ordinal) ? "OpenAI"
            : host.Contains("claude", StringComparison.Ordinal) || host.Contains("anthropic", StringComparison.Ordinal) ? "Claude" : "Gemini";
        using var query = activity.Begin("fake-status/" + provider);
        return await base.SendAsync(request, token).ConfigureAwait(false);
    }
}

internal sealed class SmokeDiagnostics : IDisposable
{
    private readonly TrayApplicationContext context;
    private readonly SmokeQueryActivity activity;
    private readonly Button refreshButton;
    private bool disposed;
    internal string Phase { get; set; } = "startup";
    internal int RefreshClicks { get; private set; }
    internal int QuotaClicks { get; private set; }

    internal SmokeDiagnostics(TrayApplicationContext context, SmokeQueryActivity activity)
    {
        this.context = context;
        this.activity = activity;
        refreshButton = context.StatusWindow.Controls.OfType<Button>().Single(button => button.Text == "Refresh");
        context.StatusWindow.VisibleChanged += OnVisibleChanged;
        context.StatusWindow.Activated += OnActivated;
        context.StatusWindow.Deactivate += OnDeactivate;
        context.StatusWindow.QuotaButton.Click += OnQuotaClick;
        context.StatusWindow.QuotaButton.TextChanged += OnQuotaTextChanged;
        refreshButton.Click += OnRefreshClick;
        refreshButton.EnabledChanged += OnRefreshEnabledChanged;
        if (context.ProviderMonitor is { } official) official.Changed += ObserveOfficial;
        if (context.QuotaMonitor is { } quota) quota.Changed += ObserveQuotas;
        ObserveOfficial();
        ObserveQuotas();
    }

    internal void Record(string point, object? detail = null)
    {
        try { RecordState(point, detail); }
        catch (Exception error) { activity.Write("diagnostic.unavailable", new { point, error = error.ToString() }); }
    }

    private void RecordState(string point, object? detail)
    {
        if (disposed || context.StatusWindow.IsDisposed) return;
        var window = context.StatusWindow;
        activity.Write(point, new
        {
            phase = Phase,
            formVisible = window.Visible,
            containsFocus = window.ContainsFocus,
            activeControl = window.ActiveControl?.Text,
            quotaToggle = window.QuotaButton.Text,
            quotaVisible = window.QuotaView.Visible,
            visibleStatusPanels = window.Controls.OfType<Panel>().Count(panel => panel != window.QuotaView && panel.Visible),
            refreshClicks = RefreshClicks,
            quotaClicks = QuotaClicks,
            buttons = window.Controls.OfType<Button>().Select(button => new { button.Text, button.Visible, button.Enabled, button.CanSelect }).ToArray(),
            captions = window.Controls.OfType<Label>().Select(label => label.Text).ToArray(),
            officialRefreshing = context.ProviderMonitor?.IsRefreshing,
            quotas = context.QuotaMonitor?.Snapshot().Select(state => new { provider = state.Provider.ToString(), state.IsRefreshing, state.IsPrevious }).ToArray(),
            queries = activity.Snapshot(),
            detail
        });
    }

    private void ObserveOfficial() => activity.ObserveRefreshing("monitor-status/all", context.ProviderMonitor?.IsRefreshing ?? false);
    private void ObserveQuotas()
    {
        if (context.QuotaMonitor is not { } monitor) return;
        foreach (var state in monitor.Snapshot()) activity.ObserveRefreshing("monitor-quota/" + state.Provider, state.IsRefreshing);
    }
    private void OnVisibleChanged(object? sender, EventArgs e) => Record("window.visible-changed");
    private void OnActivated(object? sender, EventArgs e) => Record("window.activated");
    private void OnDeactivate(object? sender, EventArgs e) => Record("window.deactivated");
    private void OnQuotaTextChanged(object? sender, EventArgs e) => Record("quota.text-changed");
    private void OnRefreshEnabledChanged(object? sender, EventArgs e) => Record("refresh.enabled-changed");
    private void OnRefreshClick(object? sender, EventArgs e) { RefreshClicks++; Record("refresh.click"); }
    private void OnQuotaClick(object? sender, EventArgs e) { QuotaClicks++; Record("quota.click", new { stack = Environment.StackTrace }); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        context.StatusWindow.VisibleChanged -= OnVisibleChanged;
        context.StatusWindow.Activated -= OnActivated;
        context.StatusWindow.Deactivate -= OnDeactivate;
        context.StatusWindow.QuotaButton.Click -= OnQuotaClick;
        context.StatusWindow.QuotaButton.TextChanged -= OnQuotaTextChanged;
        refreshButton.Click -= OnRefreshClick;
        refreshButton.EnabledChanged -= OnRefreshEnabledChanged;
        if (context.ProviderMonitor is { } official) official.Changed -= ObserveOfficial;
        if (context.QuotaMonitor is { } quota) quota.Changed -= ObserveQuotas;
    }
}
