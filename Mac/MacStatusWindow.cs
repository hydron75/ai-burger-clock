using System.Globalization;
using System.Text;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AiBurgerClock;

// Native controls only; the scroll area keeps model-scoped quota rows from enlarging the window.
internal sealed class MacStatusWindow : IDisposable
{
    internal NSWindow Window { get; }
    private readonly NSTextField schedule;
    private readonly NSTextField countdown;
    private readonly NSTextField next;
    private readonly NSTextField usTime;
    private readonly NSTextField extended;
    private readonly NSTextField checkedAt;
    private readonly NSTextField nextCheck;
    private readonly NSTextField feedback;
    private readonly NSButton refresh;
    private readonly NSButton holiday;
    private readonly NSButton autoStart;
    private readonly Dictionary<ProviderKind, (NSButton Heading, NSTextField Detail)> providers = [];
    private readonly Dictionary<QuotaProvider, NSTextView> quotas = [];

    internal MacStatusWindow(Action requestRefresh, Action showStatistics, Action<bool> changeHoliday,
        Action<bool> changeAutoStart, Action<ProviderKind> openPage,
        Action<ProviderKind, UsageEventType, bool> record, bool smoke)
    {
        Window = new(new CGRect(0, 0, 430, 720), NSWindowStyle.Titled | NSWindowStyle.Closable,
            NSBackingStore.Buffered, false)
        {
            Title = "AI Burger Clock · macOS preview",
            // Closing hides the window; the host retains and reopens it from the menu bar.
            DangerousReleasedWhenClosed = false
        };
        var root = new NSView(new CGRect(0, 0, 430, 720));
        Window.ContentView = root;
        var document = new NSView(new CGRect(0, 0, 410, 860));
        var scroll = new NSScrollView(new CGRect(0, 92, 430, 628))
        {
            HasVerticalScroller = true, AutohidesScrollers = true, DocumentView = document
        };
        root.AddSubview(scroll);
        Label(document, "AI AGENT TRAFFIC", 824, 22, 13, true);
        schedule = Label(document, "FULL THROTTLE", 785, 31, 22, true);
        countdown = Label(document, "전환까지 —", 756, 23, 15);
        next = Label(document, "다음: — KST", 731, 22);
        usTime = Label(document, "US: —", 707, 22);
        extended = Label(document, "", 670, 34, 11);
        int y = 638;
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            ProviderKind captured = provider;
            NSButton heading = Button(document, provider + " · CHECK ↗", new CGRect(14, y, 302, 27), () => openPage(captured));
            heading.Bordered = false;
            heading.Alignment = NSTextAlignment.Left;
            heading.Font = Font(13, true);
            var detail = Label(document, "Official: UNKNOWN · 확인 불가", y - 51, 50, 12);
            detail.ToolTip = "클릭하면 공식 상태 페이지를 엽니다. 사용 경험은 기록 버튼이나 메뉴바에서 남깁니다.";
            var recordButton = new NSButton(new CGRect(332, y, 58, 27))
            {
                Title = "기록", BezelStyle = NSBezelStyle.Rounded
            };
            document.AddSubview(recordButton);
            var eventMenu = RecordMenu(provider, record);
            recordButton.Activated += (_, _) => NSMenu.PopUpContextMenu(eventMenu,
                NSApplication.SharedApplication.CurrentEvent!, recordButton);
            providers.Add(provider, (heading, detail));
            y -= 91;
        }
        Label(document, "개인 계정 잔여 한도", 345, 21, 12, true);
        Label(document, "ChatGPT", 320, 23, 13, true);
        Label(document, "Work/Codex", 301, 21, 11);
        quotas[QuotaProvider.Codex] = TextArea(document, new CGRect(14, 191, 382, 108), 11);
        Label(document, "Claude", 165, 23, 13, true);
        quotas[QuotaProvider.Claude] = TextArea(document, new CGRect(14, 52, 382, 111), 11);
        checkedAt = Label(document, "Checked: — KST", 29, 19, 10);
        nextCheck = Label(document, "Next check: — KST", 9, 19, 10);
        scroll.ContentView.ScrollToPoint(new CGPoint(0, 860 - 628));
        scroll.ReflectScrolledClipView(scroll.ContentView);

        refresh = Button(root, "Refresh", new CGRect(14, 58, 107, 28), requestRefresh);
        Button(root, "Statistics", new CGRect(126, 58, 107, 28), showStatistics);
        holiday = Button(root, "미국 연방 공휴일 보정", new CGRect(14, 31, 224, 24),
            () => changeHoliday(holiday!.State == NSCellStateValue.On));
        holiday.SetButtonType(NSButtonType.Switch);
        holiday.Enabled = false;
        autoStart = Button(root, "로그인 시 자동 실행", new CGRect(14, 7, 254, 24),
            () => changeAutoStart(autoStart!.State == NSCellStateValue.On));
        autoStart.SetButtonType(NSButtonType.Switch);
        autoStart.Enabled = !smoke;
        feedback = Label(root, "", 2, 48, 10);
        feedback.Frame = new CGRect(271, 7, 148, 76);
        Window.Center();
    }

    internal void Show()
    {
        NSApplication.SharedApplication.Activate();
        Window.MakeKeyAndOrderFront(null);
    }

    internal void Update(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> states,
        IReadOnlyList<QuotaState> quotaStates, bool refreshing, DateTimeOffset? due, string storageError)
    {
        schedule.StringValue = TrayPresentation.StateName(snapshot.State);
        schedule.TextColor = Color(TrayPresentation.StateColor(snapshot.State));
        countdown.StringValue = "전환까지  " + DisplayFormatting.FormatRemaining(snapshot.Remaining);
        next.StringValue = $"다음: {snapshot.NextTransitionKst:ddd HH:mm} KST";
        string mode = snapshot.EasternIsDst == snapshot.PacificIsDst
            ? snapshot.EasternIsDst ? "DST" : "Standard" : "Mixed DST";
        usTime.StringValue = $"US: {mode} · ET {DisplayFormatting.Offset(snapshot.EasternUtcOffsetMinutes)} / PT {DisplayFormatting.Offset(snapshot.PacificUtcOffsetMinutes)}";
        usTime.ToolTip = $"Eastern: {snapshot.EasternLocalTime:yyyy-MM-dd HH:mm zzz}\nPacific: {snapshot.PacificLocalTime:yyyy-MM-dd HH:mm zzz}\n{snapshot.SchedulePolicyVersion}";
        extended.StringValue = snapshot.IsHolidayExtendedFullThrottle
            ? "공휴일 연장: " + snapshot.HolidayNames
            : snapshot.IsWeekendExtendedFullThrottle ? "Weekend / Extended Full Throttle" : "";
        foreach (ProviderStatus state in states)
        {
            var row = providers[state.Provider];
            Recommendation recommendation = RecommendationPolicy.Calculate(snapshot.State, state.Status);
            row.Heading.Title = state.Provider + "  " + RecommendationPolicy.Label(recommendation) + " ↗";
            row.Heading.ContentTintColor = RecommendationColor(recommendation);
            row.Detail.StringValue = "Official: " + RecommendationPolicy.OfficialLabel(state.Status) + "\n" + state.Reason;
            row.Detail.ToolTip = state.Reason + "\n" + state.RelevantComponent + "\n" + state.IncidentTitle;
        }
        foreach (QuotaState state in quotaStates)
        {
            quotas[state.Provider].Value = QuotaText(state, snapshot.NowUtc);
            quotas[state.Provider].ToolTip = "공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.\n" +
                "기본 6시간 · 잔여 0% 초과~10% 미만은 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분.\n" +
                "리셋 시각 경과만으로 한도 회복을 가정하지 않습니다. 크레딧·리셋 알림은 제외합니다.\n" +
                state.Error + "\n" + state.CacheError;
        }
        DateTimeOffset latest = states.Max(state => state.CheckedAtUtc);
        checkedAt.StringValue = "Checked: " + (latest == DateTimeOffset.MinValue ? "—" : AgentSchedule.ToKst(latest).ToString("HH:mm:ss")) + " KST";
        nextCheck.StringValue = "Next check: " + (due is { } at ? AgentSchedule.ToKst(at).ToString("HH:mm:ss") : refreshing ? "확인 중" : "—") + " KST";
        // Quota commands coalesce their own refreshes; do not block official-status Refresh.
        refresh.Enabled = !refreshing;
        if (storageError.Length > 0) SetFeedback(storageError);
    }

    internal void SetHoliday(bool enabled, bool ready)
    {
        holiday.State = enabled ? NSCellStateValue.On : NSCellStateValue.Off;
        holiday.Enabled = ready;
    }

    internal void SetAutoStart(bool enabled, string detail)
    {
        autoStart.State = enabled ? NSCellStateValue.On : NSCellStateValue.Off;
        autoStart.Title = detail;
        autoStart.ToolTip = ".app를 고정된 위치에 둔 뒤 켜세요. 시스템 설정 → 일반 → 로그인 항목에서 승인 상태를 확인할 수 있습니다.";
    }

    internal void SetFeedback(string text) => feedback.StringValue = text;

    internal static string QuotaText(QuotaState state, DateTimeOffset now)
    {
        var text = new StringBuilder();
        if (state.IsRefreshing) text.AppendLine("확인 중…");
        if (state.Reading is { } reading)
            foreach (QuotaWindow window in reading.Windows)
            {
                bool old = state.IsPrevious || window.ResetsAtUtc <= now;
                text.AppendLine($"{window.Label}: {window.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture)}% {(old ? "(이전)" : "남음")} · {DisplayFormatting.ResetCountdown(window.ResetsAtUtc, now)}");
            }
        else text.AppendLine(state.Error.Length > 0 ? state.Error : "한도 조회 대기 · 공식 CLI 로그인 필요");
        string success = state.LastSuccessfulCheckUtc is { } at ? AgentSchedule.ToKst(at).ToString("MM-dd HH:mm") : "—";
        string next = state.NextCheckUtc is { } due ? AgentSchedule.ToKst(due).ToString("MM-dd HH:mm") : "—";
        text.Append($"성공 {success} · 다음 {next} KST");
        return text.ToString();
    }

    internal static NSMenu RecordMenu(ProviderKind provider, Action<ProviderKind, UsageEventType, bool> record)
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        foreach (UsageEventType type in Enum.GetValues<UsageEventType>())
        {
            UsageEventType captured = type;
            menu.AddItem(new NSMenuItem(type.ToString(), (_, _) => record(provider, captured, false)));
        }
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("메모와 함께 기록…", (_, _) => record(provider, UsageEventType.Success, true)));
        return menu;
    }

    internal static NSTextField Label(NSView view, string text, int y, int height, int size = 12, bool bold = false)
    {
        var label = new NSTextField(new CGRect(14, y, view.Frame.Width - 28, height))
        {
            StringValue = text, Editable = false, Selectable = true, Bordered = false, Bezeled = false,
            DrawsBackground = false, Font = Font(size, bold),
            TextColor = NSColor.Label, UsesSingleLineMode = false, LineBreakMode = NSLineBreakMode.ByWordWrapping
        };
        view.AddSubview(label);
        return label;
    }

    internal static NSButton Button(NSView view, string title, CGRect frame, Action action)
    {
        var button = new NSButton(frame) { Title = title, BezelStyle = NSBezelStyle.Rounded };
        button.Activated += (_, _) => action();
        view.AddSubview(button);
        return button;
    }

    internal static NSTextView TextArea(NSView view, CGRect frame, int size)
    {
        var scroll = new NSScrollView(frame) { HasVerticalScroller = true, AutohidesScrollers = true };
        var text = new NSTextView(new CGRect(0, 0, frame.Width - 16, frame.Height))
        {
            Editable = false, Selectable = true, RichText = false, VerticallyResizable = true,
            HorizontallyResizable = false, Font = Font(size),
            TextColor = NSColor.Label, BackgroundColor = NSColor.WindowBackground
        };
        text.TextContainer!.WidthTracksTextView = true;
        text.TextContainer.Size = new CGSize(frame.Width - 16, float.MaxValue);
        text.MaxSize = new CGSize(frame.Width - 16, float.MaxValue);
        scroll.DocumentView = text;
        view.AddSubview(scroll);
        return text;
    }

    internal static NSColor Color(System.Drawing.Color color) => NSColor.FromRgb(color.R, color.G, color.B);

    private static NSFont Font(int size, bool bold = false) =>
        (bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size)) ??
        throw new InvalidOperationException("macOS 기본 글꼴을 불러오지 못했습니다.");

    private static NSColor RecommendationColor(Recommendation recommendation) => recommendation switch
    {
        Recommendation.Go => NSColor.SystemGreen,
        Recommendation.Hold or Recommendation.BurgerTime => NSColor.SystemOrange,
        Recommendation.Stop or Recommendation.BurgerServiceIssue => NSColor.SystemRed,
        _ => NSColor.SecondaryLabel
    };

    public void Dispose()
    {
        Window.Close();
        Window.Dispose();
    }
}
