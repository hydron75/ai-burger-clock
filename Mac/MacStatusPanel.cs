using AppKit;
using CoreGraphics;
using Foundation;

namespace AiBurgerClock;

// Left-click popover in the Windows panel order: schedule, provider cards, quotas, check times,
// buttons, feedback, options. Text and tones come from the shared panel models; this file only
// lays them out with AppKit and maps tones to system colors.
internal sealed class MacStatusPanel : IDisposable
{
    internal const int PanelWidth = 380;
    internal const int MaximumHeight = 760;
    private const int Inset = 14;
    private const int ContentWidth = PanelWidth - Inset * 2;
    private const int CardPadding = 8;
    private const int QuotaHeight = 172;
    internal const string DefaultFeedback = "Provider 이름 클릭: 공식 페이지 · 우클릭: 기록";

    private readonly NSPopover popover;
    private readonly NSStackView root;
    private readonly NSTextField state;
    private readonly NSTextField countdown;
    private readonly NSTextField next;
    private readonly NSTextField timeZone;
    private readonly NSTextField checkedCaption;
    private readonly NSTextField feedback;
    private readonly NSButton refresh;
    private readonly NSButton holiday;
    private readonly NSButton autoStart;
    private readonly NSScrollView quotaScroll;
    private readonly NSStackView quotaStack;
    private readonly Dictionary<ProviderKind, Card> cards = [];
    private readonly Dictionary<QuotaProvider, QuotaViews> quotas = [];
    private string quotaLayoutKey = "";
    private string shownStorageError = "";

    private sealed record Card(NSView View, NSButton Heading, NSTextField Official, NSTextField Reason, NSMenu Menu);
    private sealed record QuotaViews(NSTextField Heading, NSTextField? Scope, Dictionary<string, NSTextField> Rows, NSTextField Metadata);

    internal MacStatusPanel(Action requestRefresh, Action showStatistics, Action<bool> changeHoliday,
        Action<bool> changeAutoStart, Action<ProviderKind> openPage,
        Action<ProviderKind, UsageEventType, bool> record, bool smoke)
    {
        root = Stack(vertical: true, spacing: 3);
        root.EdgeInsets = new NSEdgeInsets(12, Inset, 12, Inset);
        root.WidthAnchor.ConstraintEqualTo(PanelWidth).Active = true;

        var title = Add(Line(StatusPanelModel.Title, 11, bold: true));
        title.TextColor = NSColor.SecondaryLabel;
        state = Add(Line("", 22, bold: true));
        countdown = Add(Line("", 15));
        next = Add(Line("", 12));
        timeZone = Add(Line("", 11));
        timeZone.TextColor = NSColor.SecondaryLabel;
        root.SetCustomSpacing(10, timeZone);

        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            var card = CreateCard(provider, openPage, record);
            cards.Add(provider, card);
            root.AddArrangedSubview(card.View);
            root.SetCustomSpacing(6, card.View);
        }
        root.SetCustomSpacing(10, cards[ProviderKind.Gemini].View);

        Add(Line("개인 계정 잔여 한도", 13, bold: true));
        quotaStack = Stack(vertical: true, spacing: 1);
        var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(quotaStack);
        Pin(quotaStack, document, 0, 0);
        quotaScroll = new NSScrollView
        {
            HasVerticalScroller = true, AutohidesScrollers = true, DrawsBackground = false,
            TranslatesAutoresizingMaskIntoConstraints = false, DocumentView = document
        };
        var clip = quotaScroll.ContentView;
        document.LeadingAnchor.ConstraintEqualTo(clip.LeadingAnchor).Active = true;
        document.TrailingAnchor.ConstraintEqualTo(clip.TrailingAnchor).Active = true;
        document.TopAnchor.ConstraintEqualTo(clip.TopAnchor).Active = true;
        quotaScroll.WidthAnchor.ConstraintEqualTo(ContentWidth).Active = true;
        quotaScroll.HeightAnchor.ConstraintEqualTo(QuotaHeight).Active = true;
        root.AddArrangedSubview(quotaScroll);
        root.SetCustomSpacing(8, quotaScroll);

        checkedCaption = Add(Line(StatusPanelModel.WaitingCaption, 11, lines: 2));
        root.SetCustomSpacing(8, checkedCaption);
        refresh = PushButton("Refresh", requestRefresh);
        var buttons = Stack(vertical: false, spacing: 8);
        buttons.AddArrangedSubview(refresh);
        buttons.AddArrangedSubview(PushButton("Statistics", showStatistics));
        root.AddArrangedSubview(buttons);
        root.SetCustomSpacing(6, buttons);
        feedback = Add(Line(DefaultFeedback, 11, lines: 2));
        feedback.TextColor = NSColor.SecondaryLabel;
        feedback.ToolTip = DefaultFeedback;
        root.SetCustomSpacing(6, feedback);

        holiday = Add(Checkbox(StatusPanelModel.HolidayOption, () => changeHoliday(holiday!.State == NSCellStateValue.On)));
        holiday.ToolTip = StatusPanelModel.HolidayOptionDetail;
        holiday.Enabled = false;
        autoStart = Add(Checkbox("로그인 시 자동 실행", () => changeAutoStart(autoStart!.State == NSCellStateValue.On)));
        autoStart.Enabled = !smoke;

        popover = new NSPopover
        {
            Behavior = NSPopoverBehavior.Transient,
            // A smoke run checks Shown right after Show/Close; animation would delay the state change.
            Animates = !smoke,
            ContentViewController = new NSViewController { View = root }
        };
    }

    internal bool IsShown => popover.Shown;

    internal void Show(NSView anchor)
    {
        NSApplication.SharedApplication.Activate();
        root.LayoutSubtreeIfNeeded();
        popover.ContentSize = new CGSize(PanelWidth, Math.Min(MaximumHeight, Math.Ceiling(root.FittingSize.Height)));
        popover.Show(anchor.Bounds, anchor, NSRectEdge.MinYEdge);
    }

    internal void Close()
    {
        if (popover.Shown) popover.Close();
    }

    // Called every second while the popover is open. Text and tooltips are written only when they
    // change: reassigning an unchanged tooltip closes it while it is being read.
    internal void Update(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> states,
        IReadOnlyList<QuotaState> quotaStates, bool refreshing, DateTimeOffset? due, string storageError)
    {
        var schedule = StatusPanelModel.Schedule(snapshot);
        SetText(state, schedule.State);
        SetColor(state, MacControls.Color(schedule.StateTone));
        SetText(countdown, schedule.Countdown);
        SetText(next, schedule.Next);
        SetTip(next, schedule.NextDetail);
        SetText(timeZone, schedule.TimeZone);
        SetTip(timeZone, schedule.TimeZoneDetail);

        foreach (ProviderCardText text in StatusPanelModel.Cards(states, snapshot.State))
        {
            Card card = cards[text.Provider];
            if (card.Heading.Title != text.Heading) card.Heading.Title = text.Heading;
            card.Heading.ContentTintColor = MacControls.Color(text.HeadingTone);
            SetText(card.Official, text.Official);
            SetText(card.Reason, text.Reason);
            foreach (NSView view in new NSView[] { card.View, card.Heading, card.Official, card.Reason }) SetTip(view, text.Detail);
        }

        UpdateQuotas(quotaStates, snapshot.NowUtc);
        SetText(checkedCaption, StatusPanelModel.CheckedCaption(states, refreshing, due));
        refresh.Enabled = !refreshing;
        // Show a storage error only when it changes, so it does not overwrite later save/setting
        // feedback, and clear it once storage recovers (the Windows behavior).
        if (storageError != shownStorageError)
        {
            if (storageError.Length > 0) SetFeedback(storageError, error: true);
            else if (feedback.StringValue == shownStorageError) SetFeedback(DefaultFeedback);
            shownStorageError = storageError;
        }
    }

    private void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now)
    {
        string key = string.Join('|', states.Select(s => s.Provider + ":" +
            string.Join(',', s.Reading?.Windows.Select(w => w.Id) ?? [])));
        IReadOnlyList<QuotaSectionText> sections = QuotaPanelModel.Sections(states, now);
        if (key != quotaLayoutKey)
        {
            quotaLayoutKey = key;
            foreach (NSView view in quotaStack.ArrangedSubviews)
            {
                quotaStack.RemoveView(view);
                view.RemoveFromSuperview();
            }
            quotas.Clear();
            foreach (QuotaSectionText section in sections)
            {
                var heading = QuotaLine(12, bold: true);
                var scope = section.Scope is null ? null : QuotaLine(11);
                var rows = section.Rows.ToDictionary(row => row.WindowId, _ => QuotaLine(11));
                var metadata = QuotaLine(10);
                quotaStack.SetCustomSpacing(8, metadata);
                quotas[section.Provider] = new(heading, scope, rows, metadata);
            }
        }
        foreach (QuotaSectionText section in sections)
        {
            QuotaViews views = quotas[section.Provider];
            Apply(views.Heading, section.Heading);
            if (views.Scope is not null && section.Scope is not null) Apply(views.Scope, section.Scope);
            foreach (QuotaLine row in section.Rows) Apply(views.Rows[row.WindowId], row);
            Apply(views.Metadata, section.Metadata);
        }
    }

    private NSTextField QuotaLine(int size, bool bold = false)
    {
        var line = Line("", size, bold, width: ContentWidth - 16);
        quotaStack.AddArrangedSubview(line);
        return line;
    }

    private static void Apply(NSTextField field, QuotaLine line)
    {
        SetText(field, line.Text);
        SetColor(field, MacControls.Color(line.Tone));
        SetTip(field, line.Detail);
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

    internal void SetFeedback(string text, bool error = false)
    {
        SetText(feedback, text);
        feedback.TextColor = error ? NSColor.SystemRed : NSColor.SecondaryLabel;
        SetTip(feedback, text);
    }

    // Built from the shared item list; AppKit shows a view's Menu on right-click and control-click.
    internal static NSMenu RecordMenu(ProviderKind provider, Action<ProviderKind, UsageEventType, bool> record)
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        foreach (RecordMenuItem? item in UsageMeasurementFactory.MenuItems)
            menu.AddItem(item is null ? NSMenuItem.SeparatorItem
                : new NSMenuItem(item.Text, (_, _) => record(provider, item.Type, item.WithNote)));
        return menu;
    }

    private Card CreateCard(ProviderKind provider, Action<ProviderKind> openPage, Action<ProviderKind, UsageEventType, bool> record)
    {
        var view = new CardView { TranslatesAutoresizingMaskIntoConstraints = false };
        view.WidthAnchor.ConstraintEqualTo(ContentWidth).Active = true;
        var stack = Stack(vertical: true, spacing: 1);
        view.AddSubview(stack);
        Pin(stack, view, CardPadding, 6);
        var heading = new NSButton
        {
            Title = ProviderNames.Provider(provider), Bordered = false, Alignment = NSTextAlignment.Left,
            Font = MacControls.Font(13, bold: true), TranslatesAutoresizingMaskIntoConstraints = false
        };
        heading.WidthAnchor.ConstraintEqualTo(ContentWidth - CardPadding * 2).Active = true;
        heading.Activated += (_, _) => openPage(provider);
        var official = Line("", 11, width: ContentWidth - CardPadding * 2);
        var reason = Line("", 11, width: ContentWidth - CardPadding * 2);
        reason.TextColor = NSColor.SecondaryLabel;
        stack.AddArrangedSubview(heading);
        stack.AddArrangedSubview(official);
        stack.AddArrangedSubview(reason);
        NSMenu menu = RecordMenu(provider, record);
        foreach (NSView child in new NSView[] { view, heading, official, reason }) child.Menu = menu;
        return new(view, heading, official, reason, menu);
    }

    private T Add<T>(T view) where T : NSView
    {
        root.AddArrangedSubview(view);
        return view;
    }

    private static NSStackView Stack(bool vertical, nfloat spacing) => new()
    {
        Orientation = vertical ? NSUserInterfaceLayoutOrientation.Vertical : NSUserInterfaceLayoutOrientation.Horizontal,
        Alignment = vertical ? NSLayoutAttribute.Leading : NSLayoutAttribute.CenterY,
        Spacing = spacing,
        TranslatesAutoresizingMaskIntoConstraints = false
    };

    private static void Pin(NSView child, NSView parent, nfloat horizontal, nfloat vertical)
    {
        child.LeadingAnchor.ConstraintEqualTo(parent.LeadingAnchor, horizontal).Active = true;
        child.TrailingAnchor.ConstraintEqualTo(parent.TrailingAnchor, -horizontal).Active = true;
        child.TopAnchor.ConstraintEqualTo(parent.TopAnchor, vertical).Active = true;
        child.BottomAnchor.ConstraintEqualTo(parent.BottomAnchor, -vertical).Active = true;
    }

    // Not selectable: a selectable label would show its own Copy menu instead of the card's record menu.
    private static NSTextField Line(string text, int size, bool bold = false, int lines = 1, int width = ContentWidth)
    {
        var label = new NSTextField
        {
            StringValue = text, Editable = false, Selectable = false, Bordered = false, Bezeled = false,
            DrawsBackground = false, Font = MacControls.Font(size, bold), TextColor = NSColor.Label,
            UsesSingleLineMode = lines == 1, MaximumNumberOfLines = lines,
            LineBreakMode = lines == 1 ? NSLineBreakMode.TruncatingTail : NSLineBreakMode.ByWordWrapping,
            PreferredMaxLayoutWidth = width, TranslatesAutoresizingMaskIntoConstraints = false
        };
        label.WidthAnchor.ConstraintEqualTo(width).Active = true;
        if (lines > 1)
        {
            // Fixed height: changing feedback/caption text must not resize the open popover.
            nfloat lineHeight = (nfloat)Math.Ceiling(label.Font!.BoundingRectForFont.Height);
            label.HeightAnchor.ConstraintEqualTo(lineHeight * lines).Active = true;
        }
        return label;
    }

    private static NSButton PushButton(string title, Action action)
    {
        var button = new NSButton { Title = title, BezelStyle = NSBezelStyle.Rounded };
        button.WidthAnchor.ConstraintEqualTo(110).Active = true;
        button.Activated += (_, _) => action();
        return button;
    }

    private static NSButton Checkbox(string title, Action action)
    {
        var button = new NSButton { Title = title, Font = MacControls.Font(12) };
        button.SetButtonType(NSButtonType.Switch);
        button.Activated += (_, _) => action();
        return button;
    }

    private static void SetText(NSTextField field, string text)
    {
        if (field.StringValue != text) field.StringValue = text;
    }

    private static void SetColor(NSTextField field, NSColor color)
    {
        if (!field.TextColor!.Equals(color)) field.TextColor = color;
    }

    private static void SetTip(NSView view, string tip)
    {
        if (view.ToolTip != tip) view.ToolTip = tip;
    }

    // Rounded card background; drawn per appearance so it follows light/dark mode.
    private sealed class CardView : NSView
    {
        public override void DrawRect(CGRect dirtyRect)
        {
            NSColor.ControlBackground.SetFill();
            NSBezierPath.FromRoundedRect(Bounds, 8, 8).Fill();
        }
    }

    // Top-aligned scroll content.
    private sealed class FlippedView : NSView
    {
        public override bool IsFlipped => true;
    }

    // ---- Native smoke checks (no user data, network or settings) ----

    internal CGSize Verify(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> states, IReadOnlyList<QuotaState> quotaStates)
    {
        root.LayoutSubtreeIfNeeded();
        var schedule = StatusPanelModel.Schedule(snapshot);
        if (state.StringValue != schedule.State || countdown.StringValue != schedule.Countdown ||
            next.StringValue != schedule.Next || next.ToolTip != schedule.NextDetail || timeZone.StringValue != schedule.TimeZone)
            throw new InvalidOperationException("Schedule lines differ from the shared panel model.");
        if (cards.Count != 3)
            throw new InvalidOperationException("The popover must show three provider cards.");
        foreach (ProviderCardText text in StatusPanelModel.Cards(states, snapshot.State))
        {
            Card card = cards[text.Provider];
            if (card.Heading.Title != text.Heading || card.Official.StringValue != text.Official ||
                card.Reason.StringValue != text.Reason || card.View.ToolTip != text.Detail)
                throw new InvalidOperationException($"{text.Provider} card differs from the shared panel model.");
            string[] titles = card.Menu.Items.Select(item => item.IsSeparatorItem ? "-" : item.Title).ToArray();
            string[] expected = UsageMeasurementFactory.MenuItems.Select(item => item?.Text ?? "-").ToArray();
            if (!titles.SequenceEqual(expected) || card.Heading.Menu != card.Menu || card.Reason.Menu != card.Menu)
                throw new InvalidOperationException($"{text.Provider} record menu differs from the shared item list.");
        }
        if (cards[ProviderKind.OpenAI].Heading.Title.Split(' ')[0] != "ChatGPT")
            throw new InvalidOperationException("The OpenAI card does not show the ChatGPT product name.");
        var sections = QuotaPanelModel.Sections(quotaStates, snapshot.NowUtc);
        if (quotas.Count != sections.Count)
            throw new InvalidOperationException("Quota sections differ from the shared panel model.");
        foreach (QuotaSectionText section in sections)
        {
            QuotaViews views = quotas[section.Provider];
            if (views.Heading.StringValue != section.Heading.Text || views.Metadata.StringValue != section.Metadata.Text ||
                (views.Scope?.StringValue ?? "") != (section.Scope?.Text ?? "") ||
                section.Rows.Any(row => views.Rows[row.WindowId].StringValue != row.Text ||
                    views.Rows[row.WindowId].ToolTip != row.Detail))
                throw new InvalidOperationException($"{section.Provider} quota lines differ from the shared panel model.");
        }
        // Standard rows fit the quota box; only extra model-scoped rows scroll.
        if (quotaStack.FittingSize.Height > QuotaHeight + 0.5)
            throw new InvalidOperationException("Standard quota rows do not fit without scrolling.");
        CGSize size = root.FittingSize;
        if (size.Width != PanelWidth || size.Height > MaximumHeight)
            throw new InvalidOperationException($"Popover size {size.Width}x{size.Height} exceeds {PanelWidth}x{MaximumHeight}.");
        NSView[] views2 = root.ArrangedSubviews;
        for (int i = 0; i < views2.Length; i++)
        {
            CGRect frame = views2[i].Frame;
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 ||
                frame.Right > root.Bounds.Width + 0.5 || frame.Bottom > root.Bounds.Height + 0.5)
                throw new InvalidOperationException("A popover row is outside the visible content.");
            for (int j = i + 1; j < views2.Length; j++)
                if (frame.IntersectsWith(views2[j].Frame))
                    throw new InvalidOperationException("Popover rows overlap.");
        }
        return size;
    }

    internal string QuotaRowText(QuotaProvider provider, string windowId) => quotas[provider].Rows[windowId].StringValue;

    public void Dispose()
    {
        Close();
        popover.Dispose();
        foreach (Card card in cards.Values) card.Menu.Dispose();
    }
}
