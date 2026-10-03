using System.Globalization;
using System.Text;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AiBurgerClock;

// Keep the primary status information in one viewport; only extra quota rows can scroll.
internal sealed class MacStatusWindow : IDisposable
{
    private const int WindowWidth = 430;
    private const int WindowHeight = 720;
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
    private readonly Dictionary<QuotaProvider, (NSTextField Title, NSButton Info)> quotaTitles = [];
    private readonly Dictionary<QuotaProvider, string> quotaInfo = [];
    private NSPopover? infoPopover;

    internal MacStatusWindow(Action requestRefresh, Action showStatistics, Action<bool> changeHoliday,
        Action<bool> changeAutoStart, Action<ProviderKind> openPage,
        Action<ProviderKind, UsageEventType, bool> record, bool smoke)
    {
        Window = new(new CGRect(0, 0, WindowWidth, WindowHeight), NSWindowStyle.Titled | NSWindowStyle.Closable,
            NSBackingStore.Buffered, false)
        {
            Title = "AI Burger Clock · macOS preview",
            // Closing hides the window; the host retains and reopens it from the menu bar.
            DangerousReleasedWhenClosed = false
        };
        var root = new NSView(new CGRect(0, 0, WindowWidth, WindowHeight));
        Window.ContentView = root;
        Label(root, "AI AGENT TRAFFIC", 686, 20, 13, true);
        schedule = Label(root, "FULL THROTTLE", 654, 30, 22, true);
        countdown = Label(root, "전환까지 —", 629, 23, 14);
        countdown.Frame = new CGRect(14, 629, 205, 23);
        next = Label(root, "다음: — KST", 629, 23, 11);
        next.Frame = new CGRect(232, 629, 184, 23);
        usTime = Label(root, "US: —", 606, 20, 11);
        extended = Label(root, "", 585, 19, 11);
        extended.UsesSingleLineMode = true;
        extended.LineBreakMode = NSLineBreakMode.TruncatingTail;
        // Three sections: schedule, official service status, personal account quotas.
        Separator(root, 577);
        Label(root, "서비스 상태", 548, 22, SectionTitleSize, true);
        int y = 520;
        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            ProviderKind captured = provider;
            NSButton heading = Button(root, ProviderName(provider) + " · CHECK ↗", new CGRect(14, y, 324, 24), () => openPage(captured));
            heading.Bordered = false;
            heading.Alignment = NSTextAlignment.Left;
            heading.Font = Font(13, true);
            var detail = Label(root, "Official: UNKNOWN · 확인 불가", y - 33, 32, 12);
            detail.MaximumNumberOfLines = 2;
            detail.LineBreakMode = NSLineBreakMode.TruncatingTail;
            detail.ToolTip = "클릭하면 공식 상태 페이지를 엽니다. 사용 경험은 기록 버튼이나 메뉴바에서 남깁니다.";
            var recordButton = new NSButton(new CGRect(350, y, 66, 24))
            {
                Title = "기록", BezelStyle = NSBezelStyle.Rounded
            };
            root.AddSubview(recordButton);
            var eventMenu = RecordMenu(provider, record);
            recordButton.Activated += (_, _) => NSMenu.PopUpContextMenu(eventMenu,
                NSApplication.SharedApplication.CurrentEvent!, recordButton);
            providers.Add(provider, (heading, detail));
            y -= 62;
        }
        Separator(root, 352);
        Label(root, "개인 계정 잔여 한도", 322, 22, SectionTitleSize, true);
        quotaTitles[QuotaProvider.Codex] = QuotaTitle(root, QuotaProvider.Codex, "ChatGPT", 298);
        Label(root, "Work/Codex", 280, 17, 11);
        quotas[QuotaProvider.Codex] = TextArea(root, new CGRect(14, 224, 402, 54), 11);
        quotaTitles[QuotaProvider.Claude] = QuotaTitle(root, QuotaProvider.Claude, "Claude", 200);
        quotas[QuotaProvider.Claude] = TextArea(root, new CGRect(14, 124, 402, 74), 11);
        foreach (NSTextView quota in quotas.Values) quota.TextContainerInset = new CGSize(2, 2);
        Separator(root, 112);
        checkedAt = Label(root, "Checked: — KST", 86, 18, 10);
        checkedAt.Frame = new CGRect(14, 86, 190, 18);
        nextCheck = Label(root, "Next check: — KST", 86, 18, 10);
        nextCheck.Frame = new CGRect(207, 86, 209, 18);

        refresh = Button(root, "Refresh", new CGRect(14, 56, 107, 28), requestRefresh);
        Button(root, "Statistics", new CGRect(126, 56, 107, 28), showStatistics);
        holiday = Button(root, "미국 연방 공휴일 보정", new CGRect(14, 31, 224, 24),
            () => changeHoliday(holiday!.State == NSCellStateValue.On));
        holiday.SetButtonType(NSButtonType.Switch);
        holiday.Enabled = false;
        autoStart = Button(root, "로그인 시 자동 실행", new CGRect(14, 6, 224, 24),
            () => changeAutoStart(autoStart!.State == NSCellStateValue.On));
        autoStart.SetButtonType(NSButtonType.Switch);
        autoStart.Font = Font(12);
        autoStart.Enabled = !smoke;
        feedback = Label(root, "", 2, 48, 10);
        feedback.Frame = new CGRect(245, 6, 171, 76);
        feedback.MaximumNumberOfLines = 5;
        feedback.LineBreakMode = NSLineBreakMode.TruncatingTail;
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
        // Update() runs every second. Reassigning an unchanged tooltip closes it while it is shown,
        // so text and tooltips are written only when their value changes.
        SetText(schedule, TrayPresentation.StateName(snapshot.State));
        schedule.TextColor = Color(TrayPresentation.StateColor(snapshot.State));
        SetText(countdown, "전환까지  " + DisplayFormatting.FormatRemaining(snapshot.Remaining));
        SetText(next, $"다음: {snapshot.NextTransitionKst:ddd HH:mm} KST");
        string mode = snapshot.EasternIsDst == snapshot.PacificIsDst
            ? snapshot.EasternIsDst ? "DST" : "Standard" : "Mixed DST";
        SetText(usTime, $"US: {mode} · ET {DisplayFormatting.Offset(snapshot.EasternUtcOffsetMinutes)} / PT {DisplayFormatting.Offset(snapshot.PacificUtcOffsetMinutes)}");
        SetTip(usTime, $"Eastern: {snapshot.EasternLocalTime:yyyy-MM-dd HH:mm zzz}\nPacific: {snapshot.PacificLocalTime:yyyy-MM-dd HH:mm zzz}\n{snapshot.SchedulePolicyVersion}");
        SetText(extended, snapshot.IsHolidayExtendedFullThrottle
            ? "공휴일 연장: " + snapshot.HolidayNames
            : snapshot.IsWeekendExtendedFullThrottle ? "Weekend / Extended Full Throttle" : "");
        SetTip(extended, extended.StringValue);
        foreach (ProviderStatus state in states)
        {
            var row = providers[state.Provider];
            Recommendation recommendation = RecommendationPolicy.Calculate(snapshot.State, state.Status);
            string heading = ProviderName(state.Provider) + "  " + RecommendationPolicy.Label(recommendation) + " ↗";
            if (row.Heading.Title != heading) row.Heading.Title = heading;
            row.Heading.ContentTintColor = RecommendationColor(recommendation);
            SetText(row.Detail, "Official: " + RecommendationPolicy.OfficialLabel(state.Status) + "\n" + state.Reason);
            SetTip(row.Detail, state.Reason + "\n" + state.RelevantComponent + "\n" + state.IncidentTitle);
        }
        foreach (QuotaState state in quotaStates)
        {
            NSTextView view = quotas[state.Provider];
            string text = QuotaText(state, snapshot.NowUtc);
            // The reset countdown changes this text every second. Rewrite only on change and
            // restore the scroll position only if the rewrite moved it.
            if (view.Value != text)
            {
                NSClipView? clip = view.EnclosingScrollView?.ContentView;
                CGPoint origin = clip?.Bounds.Location ?? CGPoint.Empty;
                view.Value = text;
                if (clip is not null && clip.Bounds.Location != origin)
                {
                    clip.ScrollToPoint(origin);
                    view.EnclosingScrollView!.ReflectScrolledClipView(clip);
                }
            }
            string tooltip = "공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.\n" +
                "기본 6시간 · 잔여 0% 초과~10% 미만은 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분.\n" +
                "리셋 시각 경과만으로 한도 회복을 가정하지 않습니다. 크레딧·리셋 알림은 제외합니다.\n" +
                state.Error + "\n" + state.CacheError;
            // A tooltip anywhere on the box closes on its per-second countdown rewrite, so the
            // explanation lives on the unchanging title and ⓘ button; clicking ⓘ keeps it open.
            quotaInfo[state.Provider] = tooltip.TrimEnd();
            SetTip(quotaTitles[state.Provider].Title, quotaInfo[state.Provider]);
            SetTip(quotaTitles[state.Provider].Info, quotaInfo[state.Provider]);
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
        autoStart.Title = detail switch
        {
            "자동 실행: 시스템 설정 승인 필요" => "자동 실행: 승인 필요",
            "자동 실행: .app 설치 위치 확인 필요" => "자동 실행: .app 위치 확인 필요",
            _ => detail
        };
        autoStart.ToolTip = detail + "\n.app를 고정된 위치에 둔 뒤 켜세요. 시스템 설정 → 일반 → 로그인 항목에서 승인 상태를 확인할 수 있습니다.";
    }

    internal void SetFeedback(string text)
    {
        SetText(feedback, text);
        SetTip(feedback, text);
    }

    private static void SetText(NSTextField field, string text)
    {
        if (field.StringValue != text) field.StringValue = text;
    }

    private static void SetTip(NSView view, string tip)
    {
        if (view.ToolTip != tip) view.ToolTip = tip;
    }

    // Display name only: the shared ProviderKind and stored values stay "OpenAI".
    // The row names the product like Claude and Gemini do.
    internal static string ProviderName(ProviderKind provider) =>
        provider == ProviderKind.OpenAI ? "ChatGPT" : provider.ToString();

    internal void VerifyCompactLayout()
    {
        var root = Window.ContentView ?? throw new InvalidOperationException("Status content view is missing.");
        if (root.Bounds.Width != WindowWidth || root.Bounds.Height != WindowHeight ||
            providers.Count != 3 || quotas.Count != 2)
            throw new InvalidOperationException("Compact status window dimensions/rows changed.");
        if (!providers[ProviderKind.OpenAI].Heading.Title.StartsWith("ChatGPT ", StringComparison.Ordinal))
            throw new InvalidOperationException("The OpenAI row does not show the ChatGPT product name.");
        foreach (var (provider, (_, info)) in quotaTitles)
        {
            if (string.IsNullOrEmpty(info.ToolTip) || info.Image is null)
                throw new InvalidOperationException("Quota info button has no symbol or explanation.");
            ShowQuotaInfo(info, provider);
            if (infoPopover is not { Shown: true })
                throw new InvalidOperationException("Quota info popover did not open.");
            infoPopover.Close();
        }
        NSView[] controls = root.Subviews;
        NSScrollView[] scrolls = controls.OfType<NSScrollView>().ToArray();
        if (scrolls.Length != 2 || scrolls.Any(scroll => scroll.DocumentView is not NSTextView))
            throw new InvalidOperationException("The whole status window must not scroll.");
        for (int i = 0; i < controls.Length; i++)
        {
            CGRect frame = controls[i].Frame;
            if (frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 ||
                frame.X + frame.Width > root.Bounds.Width || frame.Y + frame.Height > root.Bounds.Height)
                throw new InvalidOperationException("A status control is outside the visible content view.");
            for (int j = i + 1; j < controls.Length; j++)
            {
                CGRect other = controls[j].Frame;
                if (frame.X < other.X + other.Width && other.X < frame.X + frame.Width &&
                    frame.Y < other.Y + other.Height && other.Y < frame.Y + frame.Height)
                    throw new InvalidOperationException("Status controls overlap.");
            }
        }
        // Exercise real native text metrics, not just nominal font size or frame arithmetic.
        foreach (NSTextView quota in quotas.Values)
        {
            var container = quota.TextContainer ?? throw new InvalidOperationException("Quota text container is missing.");
            var layout = quota.LayoutManager ?? throw new InvalidOperationException("Quota layout manager is missing.");
            var scroll = quota.EnclosingScrollView ?? throw new InvalidOperationException("Quota scroll view is missing.");
            layout.EnsureLayoutForTextContainer(container);
            if (layout.GetUsedRect(container).Height + quota.TextContainerInset.Height * 2 >
                scroll.ContentView.Bounds.Height + 0.5)
                throw new InvalidOperationException("Standard quota rows do not fit without scrolling.");
        }
    }

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

    private const int SectionTitleSize = 15;

    private (NSTextField Title, NSButton Info) QuotaTitle(NSView root, QuotaProvider provider, string text, int y)
    {
        NSTextField title = Label(root, text, y, 22, 13, true);
        title.SizeToFit();
        title.Frame = new CGRect(14, y, Math.Ceiling(title.Frame.Width) + 4, 22);
        var info = new NSButton(new CGRect(title.Frame.Right + 2, y + 1, 20, 20))
        {
            Bordered = false, Title = "", ImagePosition = NSCellImagePosition.ImageOnly,
            Image = NSImage.GetSystemSymbol("info.circle", "한도 조회 설명"),
            ContentTintColor = NSColor.SecondaryLabel
        };
        info.Activated += (_, _) => ShowQuotaInfo(info, provider);
        root.AddSubview(info);
        return (title, info);
    }

    private void ShowQuotaInfo(NSButton anchor, QuotaProvider provider)
    {
        infoPopover?.Close();
        NSTextField text = NSTextField.CreateWrappingLabel(quotaInfo.GetValueOrDefault(provider, ""));
        text.Font = Font(12);
        text.Frame = new CGRect(12, 10, 336, 100);
        var content = new NSView(new CGRect(0, 0, 360, 120));
        content.AddSubview(text);
        infoPopover = new NSPopover
        {
            Behavior = NSPopoverBehavior.Transient,
            ContentViewController = new NSViewController { View = content }
        };
        infoPopover.Show(anchor.Bounds, anchor, NSRectEdge.MaxYEdge);
    }

    private static void Separator(NSView view, int y) =>
        view.AddSubview(new NSBox(new CGRect(14, y, view.Frame.Width - 28, 1)) { BoxType = NSBoxType.NSBoxSeparator });

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
        infoPopover?.Close();
        infoPopover?.Dispose();
        Window.Close();
        Window.Dispose();
    }
}
