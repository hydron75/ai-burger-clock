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
    private const int Inset = 14;
    private const int ContentWidth = PanelWidth - Inset * 2;
    private const int CardPadding = 8;
    // Quota area grows with its boxes up to this height; only extra model-scoped rows scroll.
    // 340pt holds the three standard boxes (ChatGPT, Claude, Gemini: about 326pt with usage bars).
    private const int MaximumQuotaHeight = 340;
    // On a short screen the quota area gives up height first, down to about one box. Below that the whole panel
    // scrolls (four provider cards plus a box do not fit a 1024x640 screen), with the quota area at its full height.
    private const int MinimumQuotaHeight = 90;
    // Gap between the card heading and the xAI usage link on the heading line.
    private const int HeadingGap = 8;
    // Room kept for the popover arrow and the gap below the menu bar.
    private const int ScreenMargin = 24;
    internal const string DefaultFeedback = "Provider 카드 클릭: 공식 페이지 · 우클릭: 기록";

    private readonly NSPopover popover;
    private readonly NSView content;
    private readonly NSScrollView pageScroll;
    private readonly NSView page;
    private readonly NSStackView root;
    private readonly NSLayoutConstraint quotaHeight;
    private readonly NSTextField title;
    private readonly NSTextField quotaTitle;
    private nfloat availableHeight = 760;
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
    // Rows hidden from accessibility because their bar speaks the whole row.
    private readonly HashSet<QuotaRowView> spokenByBar = [];
    private string quotaLayoutKey = "";
    private string shownStorageError = "";

    private sealed record Card(CardView View, NSTextField Heading, NSTextField Official, NSTextField Reason, NSMenu Menu,
        UsageLink? Link = null);
    private sealed record QuotaViews(CardView Box, NSTextField Heading, NSTextField? Scope,
        Dictionary<string, QuotaRowView> Rows, Dictionary<string, UsageBarView> Bars, NSTextField Metadata);

    internal MacStatusPanel(Action requestRefresh, Action showStatistics, Action<bool> changeHoliday,
        Action<bool> changeAutoStart, Action<ProviderKind> openPage, Action openXaiUsage,
        Action<ProviderKind, UsageEventType, bool> record, bool smoke)
    {
        this.smoke = smoke;
        root = Stack(vertical: true, spacing: 3);
        root.EdgeInsets = new NSEdgeInsets(12, Inset, 12, Inset);
        root.WidthAnchor.ConstraintEqualTo(PanelWidth).Active = true;

        // Alignment rule: section titles sit on the outer edge; items sit in boxes whose text starts
        // CardPadding further in. Provider cards and quota boxes share the same box and padding.
        title = Add(Line(StatusPanelModel.Title, 11, bold: true));
        title.TextColor = NSColor.SecondaryLabel;
        state = Add(Line("", 22, bold: true));
        countdown = Add(Line("", 15));
        next = Add(Line("", 12));
        timeZone = Add(Line("", 11));
        timeZone.TextColor = NSColor.SecondaryLabel;
        root.SetCustomSpacing(10, timeZone);

        foreach (ProviderKind provider in Enum.GetValues<ProviderKind>())
        {
            var card = CreateCard(provider, openPage, openXaiUsage, record);
            cards.Add(provider, card);
            root.AddArrangedSubview(card.View);
            root.SetCustomSpacing(6, card.View);
        }
        // The last provider card (xAI) ends the card group; the quota title follows with the wider gap.
        root.SetCustomSpacing(10, cards[Enum.GetValues<ProviderKind>()[^1]].View);

        quotaTitle = Add(Line(QuotaPanelModel.AccessibleName, 13, bold: true));
        root.SetCustomSpacing(6, quotaTitle);
        quotaStack = Stack(vertical: true, spacing: 6);
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
        quotaHeight = quotaScroll.HeightAnchor.ConstraintEqualTo(0);
        quotaHeight.Active = true;
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

        // Opaque background under the stack: the default popover material is translucent.
        content = new BackgroundView();
        // The stack sits in a scroll view: when the screen is too short for the cards, the quota area and the
        // controls, the whole panel scrolls instead of being cut off. Overlay scrollers take no width, so the
        // stack keeps exactly PanelWidth.
        page = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
        page.AddSubview(root);
        Pin(root, page, 0, 0);
        pageScroll = new NSScrollView
        {
            HasVerticalScroller = true, HasHorizontalScroller = false, AutohidesScrollers = true, DrawsBackground = false,
            ScrollerStyle = NSScrollerStyle.Overlay, VerticalScrollElasticity = NSScrollElasticity.None,
            HorizontalScrollElasticity = NSScrollElasticity.None,
            TranslatesAutoresizingMaskIntoConstraints = false, DocumentView = page
        };
        var pageClip = pageScroll.ContentView;
        page.LeadingAnchor.ConstraintEqualTo(pageClip.LeadingAnchor).Active = true;
        page.TrailingAnchor.ConstraintEqualTo(pageClip.TrailingAnchor).Active = true;
        page.TopAnchor.ConstraintEqualTo(pageClip.TopAnchor).Active = true;
        content.AddSubview(pageScroll);
        Pin(pageScroll, content, 0, 0);
        popover = new NSPopover
        {
            Behavior = NSPopoverBehavior.Transient,
            // A smoke run checks Shown right after Show/Close; animation would delay the state change.
            Animates = !smoke,
            ContentViewController = new NSViewController { View = content }
        };
    }

    internal bool IsShown => popover.Shown;

    // A smoke run never activates the app or takes key focus (see MacScreenGuard); only a real status-item click does.
    private readonly bool smoke;

    internal void Show(NSView anchor)
    {
        // A status-item click does not activate an accessory app, and a popover in an inactive app is
        // not key: tooltips stay hidden and checkboxes draw gray until the first click inside it.
        // Activate the app explicitly and make the popover window key as soon as it is shown.
        // Not in a smoke run: there is no click, and taking the focus away from the user's apps (a full-screen one
        // above all) is what the smoke must not do.
        if (!smoke)
        {
#pragma warning disable CA1422 // Cooperative Activate() alone left the app inactive on macOS 27.
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422
        }
        // Fit the screen under the clicked menu bar, not a fixed maximum.
        NSScreen? screen = anchor.Window?.Screen ?? NSScreen.MainScreen;
        if (screen is not null) availableHeight = screen.VisibleFrame.Height - ScreenMargin;
        FitContent();
        popover.Show(anchor.Bounds, anchor, NSRectEdge.MinYEdge);
        if (!smoke) popover.ContentViewController.View.Window?.MakeKeyWindow();
    }

    internal NSWindow? Window => popover.Shown ? popover.ContentViewController.View.Window : null;

    // The quota area fits its boxes (no gap, no scroll) unless the screen is too short; then it alone
    // shrinks and scrolls (down to MinimumQuotaHeight) so the rest of the popover is never cut off. If that is
    // still too tall, the quota area keeps its full height and the whole panel scrolls (one scroller, no nesting).
    private void FitContent()
    {
        nfloat quota = (nfloat)Math.Min(MaximumQuotaHeight, Math.Ceiling(quotaStack.FittingSize.Height));
        quotaHeight.Constant = quota;
        content.LayoutSubtreeIfNeeded();
        nfloat height = (nfloat)Math.Ceiling(root.FittingSize.Height);
        if (height > availableHeight && quota - (height - availableHeight) >= MinimumQuotaHeight)
        {
            quotaHeight.Constant = quota - (height - availableHeight);
            content.LayoutSubtreeIfNeeded();
            height = (nfloat)Math.Ceiling(root.FittingSize.Height);
        }
        nfloat visible = (nfloat)Math.Min(height, availableHeight);
        popover.ContentSize = new CGSize(PanelWidth, visible);
        // Smoke with the popover not shown (screen checks skipped): give the content the size the popover would,
        // so the layout checks run on real frames without a window.
        if (smoke && !popover.Shown)
        {
            content.Frame = new CGRect(0, 0, PanelWidth, visible);
            content.LayoutSubtreeIfNeeded();
        }
    }

    // Smoke: lay the popover out for a given usable screen height and return its content size.
    internal CGSize FitFor(nfloat usableHeight)
    {
        availableHeight = usableHeight;
        FitContent();
        return popover.ContentSize;
    }

    internal void Close()
    {
        if (popover.Shown) popover.Close();
    }

    // Called every second while the popover is open. Text and tooltips are written only when they
    // change: reassigning an unchanged tooltip closes it while it is being read.
    internal void Update(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> states,
        IReadOnlyList<QuotaState> quotaStates, bool refreshing, DateTimeOffset? due, string storageError,
        IReadOnlyDictionary<QuotaProvider, QuotaEarlyResetAlert>? recentEarlyResets = null)
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
            SetText(card.Heading, text.Heading);
            SetColor(card.Heading, MacControls.Color(text.HeadingTone));
            if (card.View.AccessibilityLabel != text.Heading) card.View.AccessibilityLabel = text.Heading;
            SetText(card.Official, text.Official);
            SetText(card.Reason, text.Reason);
            foreach (NSView view in new NSView[] { card.View, card.Heading, card.Official, card.Reason }) SetTip(view, text.Detail);
        }

        UpdateQuotas(quotaStates, snapshot.NowUtc, recentEarlyResets);
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

    private void UpdateQuotas(IReadOnlyList<QuotaState> states, DateTimeOffset now,
        IReadOnlyDictionary<QuotaProvider, QuotaEarlyResetAlert>? recentEarlyResets)
    {
        IReadOnlyList<QuotaSectionText> sections = QuotaPanelModel.Sections(states, now, recentEarlyResets);
        // Shared structural key (counts and length-prefixed IDs): window IDs are opaque, so a delimiter-joined key
        // could call two different structures the same and leave stale rows. Values and text do not change it.
        string key = QuotaPanelModel.LayoutKey(sections);
        if (key != quotaLayoutKey)
        {
            quotaLayoutKey = key;
            foreach (NSView view in quotaStack.ArrangedSubviews)
            {
                quotaStack.RemoveView(view);
                view.RemoveFromSuperview();
            }
            quotas.Clear();
            spokenByBar.Clear();
            foreach (QuotaSectionText section in sections)
            {
                // Same box as a provider card, without click, hover or menu: quotas are read-only.
                var (box, lines) = Box(press: null);
                var heading = QuotaLine(lines, 12, bold: true);
                var scope = section.Scope is null ? null : QuotaLine(lines, 11);
                // One usage bar under each row that has a value; a waiting or failed row has none.
                var rows = new Dictionary<string, QuotaRowView>();
                var bars = new Dictionary<string, UsageBarView>();
                foreach (QuotaLine row in section.Rows)
                {
                    var rowField = rows[row.WindowId] = new QuotaRowView(ContentWidth - CardPadding * 2);
                    lines.AddArrangedSubview(rowField);
                    if (row.UsedPercent is null) continue;
                    // The bar is this row's single accessibility element (see UsageBarView); hide the text
                    // fields explicitly, once per layout: a standard text field reads as not-an-element
                    // through the property by default, so the default cannot be relied on.
                    rowField.HideFromAccessibility();
                    spokenByBar.Add(rowField);
                    var bar = new UsageBarView(ContentWidth - CardPadding * 2);
                    lines.AddArrangedSubview(bar);
                    lines.SetCustomSpacing(3, bar);
                    bars[row.WindowId] = bar;
                }
                var metadata = QuotaLine(lines, 10);
                quotaStack.AddArrangedSubview(box);
                quotas[section.Provider] = new(box, heading, scope, rows, bars, metadata);
            }
            // Box count or rows changed: refit so no gap is left above the check times.
            FitContent();
        }
        foreach (QuotaSectionText section in sections)
        {
            QuotaViews views = quotas[section.Provider];
            // One Provider detail for the whole box: the box, its lines and its bars share it.
            string detail = section.Detail;
            SetTip(views.Box, detail);
            Apply(views.Heading, section.Heading, detail);
            if (views.Scope is not null && section.Scope is not null) Apply(views.Scope, section.Scope, detail);
            foreach (QuotaLine row in section.Rows)
            {
                views.Rows[row.WindowId].Apply(row, MacControls.Color(row.Tone), detail);
                if (views.Bars.TryGetValue(row.WindowId, out UsageBarView? bar) && row.UsedPercent is { } used)
                {
                    // One element per row: the bar speaks "row text. comparison"; its text field is hidden.
                    SetSpoken(bar, SpokenRow(row));
                    // Previous and elapsed values are shown muted, never in the provider color, and
                    // never with a time comparison (the shared model already gives null for them).
                    bool muted = row.Tone == PanelTone.Muted;
                    bar.Set(used, muted ? null : row.ElapsedPercent,
                        muted ? MacControls.Color(PanelTone.Muted) : MacControls.QuotaBarColor(section.Provider));
                    SetTip(bar, detail);
                }
            }
            Apply(views.Metadata, section.Metadata, detail);
        }
    }

    private static NSTextField QuotaLine(NSStackView box, int size, bool bold = false)
    {
        var line = Line("", size, bold, width: ContentWidth - CardPadding * 2);
        box.AddArrangedSubview(line);
        return line;
    }

    private static void Apply(NSTextField field, QuotaLine line, string detail)
    {
        SetText(field, line.Text);
        SetColor(field, MacControls.Color(line.Tone));
        SetTip(field, detail);
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

    private Card CreateCard(ProviderKind provider, Action<ProviderKind> openPage, Action openXaiUsage,
        Action<ProviderKind, UsageEventType, bool> record)
    {
        // The whole card opens the official page; a label heading keeps one click target.
        var (view, stack) = Box(press: () => openPage(provider));
        int textWidth = ContentWidth - CardPadding * 2;
        // Only the xAI card has a link: the fixed usage dashboard, to the right of the heading. It is not a quota value.
        UsageLink? link = provider == ProviderKind.Xai ? new UsageLink(ProviderStatusPages.XaiUsageLabel, openXaiUsage) : null;
        var heading = Line(ProviderNames.Provider(provider), 13, bold: true,
            width: link is null ? textWidth : textWidth - link.Width - HeadingGap);
        var official = Line("", 11, width: textWidth);
        var reason = Line("", 11, width: textWidth);
        reason.TextColor = NSColor.SecondaryLabel;
        NSStackView? headingRow = null;
        if (link is null) stack.AddArrangedSubview(heading);
        else
        {
            headingRow = Stack(vertical: false, spacing: HeadingGap);
            headingRow.AddArrangedSubview(heading);
            headingRow.AddArrangedSubview(link);
            stack.AddArrangedSubview(headingRow);
            // Clicks inside the link rectangle open the dashboard; every other click on the card opens the status page.
            view.SetLink(link);
        }
        stack.AddArrangedSubview(official);
        stack.AddArrangedSubview(reason);
        NSMenu menu = RecordMenu(provider, record);
        foreach (NSView child in new NSView?[] { view, headingRow, heading, link, official, reason }.OfType<NSView>()) child.Menu = menu;
        return new(view, heading, official, reason, menu, link);
    }

    // A rounded box with the shared inner padding; provider cards and quota boxes both use it.
    private static (CardView Box, NSStackView Lines) Box(Action? press)
    {
        var view = new CardView(press);
        view.WidthAnchor.ConstraintEqualTo(ContentWidth).Active = true;
        var stack = Stack(vertical: true, spacing: 1);
        view.AddSubview(stack);
        Pin(stack, view, CardPadding, 6);
        return (view, stack);
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

    // The row's accessibility label (what VoiceOver reads); assigned only when it changes, because
    // reassigning every tick would interrupt a reading.
    private static void SetSpoken(NSView element, string spoken)
    {
        if (element.AccessibilityLabel != spoken) element.AccessibilityLabel = spoken;
    }

    // Rounded box drawn per appearance. With a press action it is a provider card: the whole card is a
    // click target with a hover tint and pointing-hand cursor; without one it is a read-only quota box.
    private sealed class CardView : NSView
    {
        private readonly Action? press;
        private UsageLink? link;
        private NSTrackingArea? tracking;
        private bool hovered;

        internal CardView(Action? press)
        {
            this.press = press;
            TranslatesAutoresizingMaskIntoConstraints = false;
            if (press is null) return;
            // Primary button only; right-click and control-click open the record menu instead.
            AddGestureRecognizer(new NSClickGestureRecognizer(() =>
            {
                NSEvent? click = NSApplication.SharedApplication.CurrentEvent;
                if (!MacApplication.IsContextClick(click))
                    Route(click is null ? null : ConvertPointFromView(click.LocationInWindow, null));
            }) { ButtonMask = 1 });
            AccessibilityRole = NSAccessibilityRoles.ButtonRole;
        }

        internal bool IsInteractive => press is not null;

        internal void SetLink(UsageLink value) => link = value;

        // The link's rectangle in this card's coordinates.
        internal CGRect? LinkRect => link is null ? null : link.ConvertRectToView(link.Bounds, this);

        // One click, one target: a click inside the link opens the usage dashboard only, any other click on the card
        // opens the official status page only. A click without a position (no current event) is a card click.
        internal void Route(CGPoint? pointInCard)
        {
            if (press is null) return;
            if (link is not null && pointInCard is { } point && LinkRect is { } rect && rect.Contains(point)) link.Press();
            else press();
        }

        public override bool AccessibilityPerformPress()
        {
            if (press is null) return false;
            press();
            return true;
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            if (press is null) return;
            if (tracking is not null) RemoveTrackingArea(tracking);
            tracking = new NSTrackingArea(Bounds, NSTrackingAreaOptions.MouseEnteredAndExited |
                NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect, this, null);
            AddTrackingArea(tracking);
        }

        public override void MouseEntered(NSEvent theEvent) => SetHovered(true);

        public override void MouseExited(NSEvent theEvent) => SetHovered(false);

        private void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            NeedsDisplay = true;
        }

        public override void ResetCursorRects()
        {
            if (press is not null) AddCursorRect(Bounds, NSCursor.PointingHandCursor);
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            // Opaque panel background (or the hover tint); a separator outline marks the box.
            NSBezierPath path = NSBezierPath.FromRoundedRect(Bounds.Inset(0.5f, 0.5f), 8, 8);
            (hovered ? MacControls.CardHoverBackground() : MacControls.PanelBackground).SetFill();
            path.Fill();
            NSColor.Separator.SetStroke();
            path.LineWidth = 1;
            path.Stroke();
        }
    }

    // The xAI usage link: underlined system link color text. It is a plain label whose click is routed by its card
    // (CardView.Route), so the card's click recognizer and the link never both fire for one click. VoiceOver gets
    // its own link element with a press action.
    internal sealed class UsageLink : NSTextField
    {
        internal const string AccessibleHelp = "Grok 공식 대시보드를 엽니다. 한도 값은 앱에서 조회하지 않습니다.";
        private readonly Action press;

        internal UsageLink(string text, Action press)
        {
            this.press = press;
            Editable = false; Selectable = false; Bordered = false; Bezeled = false; DrawsBackground = false;
            TranslatesAutoresizingMaskIntoConstraints = false;
            UsesSingleLineMode = true;
            LineBreakMode = NSLineBreakMode.Clipping;
            AttributedStringValue = new NSAttributedString(text, new NSStringAttributes
            {
                Font = MacControls.Font(11), ForegroundColor = NSColor.Link, UnderlineStyle = (int)NSUnderlineStyle.Single
            });
            Width = (int)Math.Ceiling(IntrinsicContentSize.Width) + 2;
            WidthAnchor.ConstraintEqualTo(Width).Active = true;
            ToolTip = text + ": " + ProviderStatusPages.XaiUsage.AbsoluteUri;
            AccessibilityElement = true;
            AccessibilityRole = NSAccessibilityRoles.LinkRole;
            AccessibilityLabel = text;
            AccessibilityHelp = AccessibleHelp;
        }

        internal int Width { get; }

        internal void Press() => press();

        public override bool AccessibilityPerformPress()
        {
            press();
            return true;
        }
    }

    // Drawn per appearance, so it follows light/dark mode.
    private sealed class BackgroundView : NSView
    {
        public override void DrawRect(CGRect dirtyRect)
        {
            MacControls.PanelBackground.SetFill();
            NSGraphics.RectFill(dirtyRect);
        }
    }

    // Top-aligned scroll content.
    private sealed class FlippedView : NSView
    {
        public override bool IsFlipped => true;
    }

    // Read-only usage bar under a quota row. Layers, back to front: track, a faint provider tint up to
    // the snapshot's elapsed-time position (only when the shared model provides one), the used fill and
    // a 1pt time marker that is always on top. For VoiceOver the bar is the row's single element: it is
    // labelled with the row text followed by the comparison (see SpokenRow) while the row's text field
    // is hidden from accessibility, so the row is announced once rather than as label + value.
    internal sealed class UsageBarView : NSView
    {
        internal const int BarHeight = 5;
        internal const double MarkerThickness = 1;
        private readonly nfloat width;

        internal UsageBarView(nfloat width)
        {
            this.width = width;
            TranslatesAutoresizingMaskIntoConstraints = false;
            WidthAnchor.ConstraintEqualTo(width).Active = true;
            HeightAnchor.ConstraintEqualTo(BarHeight).Active = true;
            AccessibilityElement = true;
            AccessibilityRole = NSAccessibilityRoles.LevelIndicatorRole;
        }

        internal double Fraction { get; private set; }
        // null: no comparison for this row (previous, elapsed, unavailable); the bar keeps its plain look.
        internal double? ElapsedPercent { get; private set; }
        internal NSColor FillColor { get; private set; } = NSColor.SecondaryLabel;

        // 0% is empty and 100% is full; any other value keeps a visible fill of at least 2pt.
        internal nfloat FilledWidth => Fraction <= 0 ? 0 : Fraction >= 1 ? width
            : (nfloat)Math.Min(width, Math.Max(2, Math.Round(width * Fraction)));

        internal nfloat ElapsedWidth => ElapsedPercent is { } elapsed
            ? (nfloat)Math.Min(width, Math.Round(width * elapsed / 100, MidpointRounding.AwayFromZero)) : 0;

        // The marker is centered on the elapsed position and kept inside the bar at 0% and 100%.
        internal nfloat MarkerWidth => ElapsedPercent is null ? 0 : (nfloat)MarkerThickness;
        internal nfloat MarkerLeft => ElapsedPercent is null ? 0
            : (nfloat)Math.Clamp(ElapsedWidth - MarkerThickness / 2, 0, width - MarkerThickness);

        internal void Set(double usedPercent, double? elapsedPercent, NSColor color)
        {
            double fraction = double.IsFinite(usedPercent) ? Math.Clamp(usedPercent, 0, 100) / 100 : 0;
            double? elapsed = elapsedPercent is { } value && double.IsFinite(value) && value is >= 0 and <= 100 ? value : null;
            if (fraction == Fraction && elapsed == ElapsedPercent && FillColor.Equals(color)) return;
            Fraction = fraction;
            ElapsedPercent = elapsed;
            FillColor = color;
            NeedsDisplay = true;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            bool dark = MacControls.IsDark(EffectiveAppearance);
            MacControls.BarPalette palette = MacControls.BarPaletteFor(FillColor, dark);
            nfloat radius = Bounds.Height / 2;
            NSBezierPath track = NSBezierPath.FromRoundedRect(Bounds, radius, radius);
            palette.Track.ToColor().SetFill();
            track.Fill();
            NSGraphicsContext.GlobalSaveGraphicsState();
            track.AddClip();
            // Tint only when a mixture meets the contrast targets; otherwise the marker and text carry it.
            if (ElapsedPercent is not null && palette.HasDistinctTint && ElapsedWidth > 0)
            {
                palette.Elapsed.ToColor().SetFill();
                NSGraphics.RectFill(new CGRect(0, 0, ElapsedWidth, Bounds.Height));
            }
            if (FilledWidth > 0)
            {
                palette.Used.ToColor().SetFill();
                NSGraphics.RectFill(new CGRect(0, 0, FilledWidth, Bounds.Height));
            }
            if (ElapsedPercent is not null)
            {
                if (palette.Halo is { } halo)
                {
                    halo.ToColor().SetFill();
                    nfloat left = (nfloat)Math.Max(0, MarkerLeft - 1);
                    nfloat right = (nfloat)Math.Min(width, MarkerLeft + MarkerWidth + 1);
                    NSGraphics.RectFill(new CGRect(left, 0, right - left, Bounds.Height));
                }
                palette.Marker.ToColor().SetFill();
                NSGraphics.RectFill(new CGRect(MarkerLeft, 0, MarkerWidth, Bounds.Height));
            }
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }
    }

    // One quota row. The value (usage, previous mark, reset) keeps its full width, measured in the real 11pt
    // font first; only the name is shortened, in the middle, with the shared DisplayFormatting.MiddleEllipsis
    // and the same font measure. A row without a split (a waiting or failed row) is one tail-truncated text.
    internal sealed class QuotaRowView : NSView
    {
        private const double NameValueGap = 6;
        private readonly NSTextField name = RowText();
        private readonly NSTextField value = RowText();
        private readonly NSTextField scratch = RowText();
        private readonly NSLayoutConstraint nameWidth;
        private readonly NSLayoutConstraint valueWidth;
        private readonly double width;
        private string? shownName;
        private string? shownValue;

        internal string FullText { get; private set; } = "";
        internal bool Split { get; private set; }
        internal bool ValueFits { get; private set; } = true;
        internal string VisibleName => name.StringValue;
        internal string VisibleValue => value.Hidden ? "" : value.StringValue;
        internal double NameWidth => nameWidth.Constant;
        internal double ValueWidth => valueWidth.Constant;
        internal bool HasAccessibilityLabel => !string.IsNullOrEmpty(AccessibilityLabel) ||
            !string.IsNullOrEmpty(name.AccessibilityLabel) || !string.IsNullOrEmpty(value.AccessibilityLabel);
        internal bool IsAccessibilityElement => AccessibilityElement || name.AccessibilityElement || value.AccessibilityElement;

        private static NSTextField RowText() => new()
        {
            StringValue = "", Editable = false, Selectable = false, Bordered = false, Bezeled = false,
            DrawsBackground = false, Font = MacControls.Font(11, false), TextColor = NSColor.Label,
            UsesSingleLineMode = true, MaximumNumberOfLines = 1, LineBreakMode = NSLineBreakMode.Clipping,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        internal QuotaRowView(double width)
        {
            this.width = width;
            TranslatesAutoresizingMaskIntoConstraints = false;
            value.Alignment = NSTextAlignment.Right;
            value.LineBreakMode = NSLineBreakMode.TruncatingTail;
            AddSubview(name);
            AddSubview(value);
            nameWidth = name.WidthAnchor.ConstraintEqualTo((nfloat)width);
            valueWidth = value.WidthAnchor.ConstraintEqualTo(0);
            scratch.StringValue = "Ag가";
            // Same height a plain one-line label of this font has, so the popover height is unchanged.
            nfloat height = (nfloat)Math.Ceiling(scratch.IntrinsicContentSize.Height);
            NSLayoutConstraint.ActivateConstraints([
                WidthAnchor.ConstraintEqualTo((nfloat)width), HeightAnchor.ConstraintEqualTo(height),
                name.LeadingAnchor.ConstraintEqualTo(LeadingAnchor), name.TopAnchor.ConstraintEqualTo(TopAnchor),
                name.BottomAnchor.ConstraintEqualTo(BottomAnchor), nameWidth,
                value.TrailingAnchor.ConstraintEqualTo(TrailingAnchor), value.TopAnchor.ConstraintEqualTo(TopAnchor),
                value.BottomAnchor.ConstraintEqualTo(BottomAnchor), valueWidth]);
        }

        // The real width of a candidate text in this row's font (what AppKit lays out for one line).
        private double Measure(string text)
        {
            scratch.StringValue = text;
            return (double)scratch.IntrinsicContentSize.Width;
        }

        internal void HideFromAccessibility()
        {
            name.AccessibilityElement = false;
            value.AccessibilityElement = false;
        }

        internal void Apply(QuotaLine line, NSColor color, string detail)
        {
            FullText = line.Text;
            if (line.NameText is { } nameText && line.ValueText is { } valueText)
            {
                // Text unchanged: keep the layout, reassigning it every tick would only cost time.
                if (!Split || nameText != shownName || valueText != shownValue)
                {
                    Split = true;
                    shownName = nameText;
                    shownValue = valueText;
                    double wanted = Math.Ceiling(Measure(valueText));
                    ValueFits = wanted <= width;
                    value.Hidden = false;
                    if (value.StringValue != valueText) value.StringValue = valueText;
                    valueWidth.Constant = (nfloat)Math.Min(width, wanted);
                    double room = Math.Max(0, width - valueWidth.Constant - NameValueGap);
                    string visible = DisplayFormatting.MiddleEllipsis(nameText, room, Measure);
                    name.LineBreakMode = NSLineBreakMode.Clipping;
                    if (name.StringValue != visible) name.StringValue = visible;
                    nameWidth.Constant = (nfloat)room;
                    name.Hidden = room <= 0 || visible.Length == 0;
                }
            }
            else if (Split || shownName is null)
            {
                Split = false;
                shownName = shownValue = null;
                ValueFits = true;
                value.Hidden = true;
                value.StringValue = "";
                valueWidth.Constant = 0;
                nameWidth.Constant = (nfloat)width;
                name.Hidden = false;
                name.LineBreakMode = NSLineBreakMode.TruncatingTail;
            }
            if (!Split && name.StringValue != line.Text) name.StringValue = line.Text;
            if (!name.TextColor!.Equals(color)) name.TextColor = color;
            if (!value.TextColor!.Equals(color)) value.TextColor = color;
            if (ToolTip != detail) ToolTip = detail;
            if (name.ToolTip != detail) name.ToolTip = detail;
            if (value.ToolTip != detail) value.ToolTip = detail;
        }

        // Smoke: the painted row is exactly the shared split, the value is whole and the name fits its room.
        internal void VerifyAgainst(QuotaLine row, string owner)
        {
            void Fail(string reason) => throw new InvalidOperationException($"{owner} quota row '{row.WindowId}': {reason}");
            if (Split != (row.NameText is not null && row.ValueText is not null)) Fail("split state differs from the shared model.");
            if (!Split)
            {
                if (name.StringValue != row.Text || !value.Hidden) Fail("an unsplit row must show the plain shared text only.");
                return;
            }
            if (value.StringValue != row.ValueText || value.Hidden) Fail("the value is not shown as the shared value text.");
            double wanted = Math.Ceiling(Measure(row.ValueText!));
            if (wanted <= width && Math.Abs(ValueWidth - wanted) > 0.01) Fail("the value did not get its full measured width.");
            if (ValueFits != (wanted <= width)) Fail("the value fit flag is wrong.");
            double room = Math.Max(0, width - ValueWidth - NameValueGap);
            string expected = DisplayFormatting.MiddleEllipsis(row.NameText!, room, Measure);
            if (name.StringValue != expected || Math.Abs(NameWidth - room) > 0.01) Fail("the name is not the shared middle ellipsis of the room left by the value.");
            if (!name.Hidden && Measure(name.StringValue) > NameWidth + 0.01) Fail("the shown name is wider than its room.");
            if (name.StringValue.Length > 0 && name.StringValue != row.NameText && !name.StringValue.Contains('…') && !name.Hidden)
                Fail("a shortened name must show the ellipsis.");
            if (ValueWidth + NameValueGap + NameWidth > width + 0.01 && NameWidth > 0) Fail("name, gap and value are wider than the row.");
            if (Math.Abs(Frame.Width - width) > 0.5) Fail("the row is not as wide as the box text area.");
        }
    }

    // What VoiceOver reads for a quota row: the shared reading text once (the whole row text, and the
    // comparison with its age when there is one). It is never rebuilt from the visible, shortened name
    // and never gets the comparison appended again.
    internal static string SpokenRow(QuotaLine row) => row.ReadingText ?? row.Text;

    // ---- Native smoke checks (no user data, network or settings) ----

    internal sealed record Layout(CGSize Size, nfloat TitleX, nfloat BoxTextX, nfloat UsableHeight, nfloat BoxTextWidth);

    internal Layout Verify(ScheduleSnapshot snapshot, IReadOnlyList<ProviderStatus> states, IReadOnlyList<QuotaState> quotaStates,
        IReadOnlyDictionary<QuotaProvider, QuotaEarlyResetAlert>? recentEarlyResets = null)
    {
        content.LayoutSubtreeIfNeeded();
        var schedule = StatusPanelModel.Schedule(snapshot);
        if (state.StringValue != schedule.State || countdown.StringValue != schedule.Countdown ||
            next.StringValue != schedule.Next || next.ToolTip != schedule.NextDetail || timeZone.StringValue != schedule.TimeZone)
            throw new InvalidOperationException("Schedule lines differ from the shared panel model.");
        if (cards.Count != 4 || !cards.Keys.SequenceEqual(Enum.GetValues<ProviderKind>()))
            throw new InvalidOperationException("The popover must show four provider cards (ChatGPT, Claude, Gemini, xAI).");
        foreach (ProviderCardText text in StatusPanelModel.Cards(states, snapshot.State))
        {
            Card card = cards[text.Provider];
            if (card.Heading.StringValue != text.Heading || card.Official.StringValue != text.Official ||
                card.Reason.StringValue != text.Reason || card.View.ToolTip != text.Detail ||
                card.View.AccessibilityLabel != text.Heading)
                throw new InvalidOperationException($"{text.Provider} card differs from the shared panel model.");
            string[] titles = card.Menu.Items.Select(item => item.IsSeparatorItem ? "-" : item.Title).ToArray();
            string[] expected = UsageMeasurementFactory.MenuItems.Select(item => item?.Text ?? "-").ToArray();
            if (!titles.SequenceEqual(expected) || card.Heading.Menu != card.Menu || card.Reason.Menu != card.Menu)
                throw new InvalidOperationException($"{text.Provider} record menu differs from the shared item list.");
            // The whole card is the click target: one primary-button recognizer, exposed as a button.
            if (!card.View.IsInteractive || card.View.GestureRecognizers is not [NSClickGestureRecognizer { ButtonMask: 1 }] ||
                card.View.AccessibilityRole != NSAccessibilityRoles.ButtonRole.ToString())
                throw new InvalidOperationException($"{text.Provider} card is not a whole-card click target.");
            // The heading is never cut off, also next to the xAI link.
            if (card.Heading.AttributedStringValue.Size.Width > card.Heading.Frame.Width + 0.5)
                throw new InvalidOperationException($"{text.Provider} card heading is truncated.");
            VerifyUsageLink(text.Provider, card);
        }
        if (cards[ProviderKind.OpenAI].Heading.StringValue.Split(' ')[0] != "ChatGPT")
            throw new InvalidOperationException("The OpenAI card does not show the ChatGPT product name.");
        var sections = QuotaPanelModel.Sections(quotaStates, snapshot.NowUtc, recentEarlyResets);
        if (quotas.Count != sections.Count)
            throw new InvalidOperationException("Quota sections differ from the shared panel model.");
        foreach (QuotaSectionText section in sections)
        {
            QuotaViews views = quotas[section.Provider];
            if (views.Heading.StringValue != section.Heading.Text || views.Metadata.StringValue != section.Metadata.Text ||
                (views.Scope?.StringValue ?? "") != (section.Scope?.Text ?? "") ||
                section.Rows.Any(row => views.Rows[row.WindowId].FullText != row.Text))
                throw new InvalidOperationException($"{section.Provider} quota lines differ from the shared panel model.");
            // VoiceOver: one element per row. A row with a bar is spoken by the bar (row text, then the
            // shared comparison; no comparison, no suffix) and its text field is hidden, so the visible
            // text is never announced twice; a row without a bar keeps its plain text field untouched.
            foreach (QuotaLine row in section.Rows)
            {
                QuotaRowView field = views.Rows[row.WindowId];
                bool hasSpokenBar = views.Bars.TryGetValue(row.WindowId, out UsageBarView? spokenBar);
                if (hasSpokenBar != spokenByBar.Contains(field) || field.HasAccessibilityLabel)
                    throw new InvalidOperationException($"{section.Provider} row '{row.WindowId}' text field accessibility is inconsistent " +
                        $"(hidden {spokenByBar.Contains(field)}, bar {hasSpokenBar}, label set {field.HasAccessibilityLabel}).");
                if (hasSpokenBar && (field.IsAccessibilityElement || spokenBar!.AccessibilityLabel != SpokenRow(row)))
                    throw new InvalidOperationException($"{section.Provider} row {row.WindowId} is not spoken once as the shared reading text.");
                field.VerifyAgainst(row, section.Provider.ToString());
            }
            // One Provider detail per box: the box, every line and every bar carry the same shared text.
            NSView[] tipped = new NSView?[] { views.Box, views.Heading, views.Scope, views.Metadata }
                .OfType<NSView>().Concat(views.Rows.Values).Concat(views.Bars.Values).ToArray();
            if (section.Detail.Length == 0 || tipped.Any(view => view.ToolTip != section.Detail))
                throw new InvalidOperationException($"{section.Provider} quota box does not show the single shared Provider detail.");
            // A usage bar per row with a value (none for a waiting or failed row), drawn from the model's
            // used percentage; previous/elapsed (Muted) values use the muted color, never the provider color.
            foreach (QuotaLine row in section.Rows)
            {
                bool hasBar = views.Bars.TryGetValue(row.WindowId, out UsageBarView? bar);
                if (row.UsedPercent is not { } used)
                {
                    if (hasBar) throw new InvalidOperationException($"{section.Provider} row without a value has a usage bar.");
                    continue;
                }
                NSColor expectedColor = row.Tone == PanelTone.Muted ? MacControls.Color(PanelTone.Muted)
                    : MacControls.QuotaBarColor(section.Provider);
                if (!hasBar || Math.Abs(bar!.Fraction - Math.Clamp(used, 0, 100) / 100) > 1e-9 ||
                    !bar.FillColor.Equals(expectedColor) || bar.GestureRecognizers.Length != 0 || bar.Menu is not null ||
                    !bar.AccessibilityElement)
                    throw new InvalidOperationException($"{section.Provider} usage bar for {row.WindowId} is wrong.");
                // Time layer: the shared model's elapsed percentage, hidden (plain bar, no marker) whenever
                // the row has no comparison or is shown muted (previous / reset elapsed).
                double? expectedElapsed = row.Tone == PanelTone.Muted ? null : row.ElapsedPercent;
                if (bar.ElapsedPercent != expectedElapsed)
                    throw new InvalidOperationException($"{section.Provider} {row.WindowId} bar elapsed {bar.ElapsedPercent} != {expectedElapsed}.");
                if (expectedElapsed is null)
                {
                    if (bar.ElapsedWidth != 0 || bar.MarkerWidth != 0 || row.ComparisonText is not null && row.Tone != PanelTone.Muted)
                        throw new InvalidOperationException($"{section.Provider} {row.WindowId} draws a time layer without a comparison.");
                }
                else
                {
                    nfloat barWidth = bar.Frame.Width;
                    nfloat expectedWidth = (nfloat)Math.Round(barWidth * expectedElapsed.Value / 100, MidpointRounding.AwayFromZero);
                    nfloat expectedLeft = (nfloat)Math.Clamp(expectedWidth - UsageBarView.MarkerThickness / 2, 0,
                        barWidth - UsageBarView.MarkerThickness);
                    if (Math.Abs(bar.ElapsedWidth - expectedWidth) > 0.01 || Math.Abs(bar.MarkerLeft - expectedLeft) > 0.01 ||
                        bar.MarkerWidth != UsageBarView.MarkerThickness || bar.MarkerLeft < 0 ||
                        bar.MarkerLeft + bar.MarkerWidth > barWidth + 0.01)
                        throw new InvalidOperationException($"{section.Provider} {row.WindowId} time marker is misplaced.");
                    if (expectedElapsed.Value <= 0 && bar.MarkerLeft != 0 ||
                        expectedElapsed.Value >= 100 && Math.Abs(bar.MarkerLeft + bar.MarkerWidth - barWidth) > 0.01)
                        throw new InvalidOperationException($"{section.Provider} {row.WindowId} marker is not kept inside at the end points.");
                }
                // 0% is empty, 100% is full, anything between shows a visible fill.
                nfloat filled = bar.FilledWidth;
                bool ok = used <= 0 ? filled == 0 : used >= 100 ? filled == bar.Frame.Width : filled >= 2 && filled <= bar.Frame.Width;
                if (!ok) throw new InvalidOperationException($"{section.Provider} usage bar fill {filled}pt does not match {used}%.");
            }
            // Read-only: no click, hover, cursor or record menu on a quota box.
            if (views.Box.IsInteractive || views.Box.GestureRecognizers.Length != 0 || views.Box.Menu is not null)
                throw new InvalidOperationException($"{section.Provider} quota box must not be clickable.");
        }

        // Alignment rule: titles on the outer edge, every boxed line one CardPadding further in.
        nfloat X(NSView view) => view.ConvertPointToView(CGPoint.Empty, content).X;
        nfloat titleX = X(title);
        if (Math.Abs(X(quotaTitle) - titleX) > 0.5)
            throw new InvalidOperationException($"Quota title x {X(quotaTitle)} differs from the panel title x {titleX}.");
        NSView[] boxed = cards.Values.SelectMany(card => new NSView[] { card.Heading, card.Official, card.Reason })
            .Concat(quotas.Values.SelectMany(q => new NSView?[] { q.Heading, q.Scope, q.Metadata }.OfType<NSView>()))
            .ToArray();
        // A label's frame sits a couple of points left of where its text starts (its alignment rect), so
        // a bar must start at its row's alignment-rect x, i.e. exactly where that row's text starts, and
        // span the text width. A quota row view starts exactly where its name text starts, so its frame x
        // must equal the heading's text start, and the bar starts there too.
        foreach (QuotaViews q in quotas.Values)
        {
            nfloat headingTextStart = X(q.Heading) + (q.Heading.GetAlignmentRectForFrame(q.Heading.Frame).X - q.Heading.Frame.X);
            foreach (QuotaRowView rowView in q.Rows.Values)
                if (Math.Abs(X(rowView) - headingTextStart) > 0.5)
                    throw new InvalidOperationException($"A quota row starts at x {X(rowView)}, not at the heading text start {headingTextStart}.");
            foreach (var (windowId, bar) in q.Bars)
            {
                QuotaRowView rowField = q.Rows[windowId];
                nfloat textStart = rowField.Frame.X;
                if (Math.Abs(bar.Frame.X - textStart) > 0.5 || Math.Abs(bar.Frame.Width - (ContentWidth - CardPadding * 2)) > 0.5)
                    throw new InvalidOperationException(
                        $"A usage bar does not line up with its row text (bar x {bar.Frame.X}, text x {textStart}, width {bar.Frame.Width}).");
            }
        }
        nfloat boxX = X(cards[ProviderKind.OpenAI].Heading);
        if (boxed.Any(view => Math.Abs(X(view) - boxX) > 0.5) || Math.Abs(boxX - titleX - CardPadding) > 0.5)
            throw new InvalidOperationException("Card and quota box text do not start at the same x: " +
                string.Join(", ", boxed.Select(X).Distinct().Select(x => x.ToString("0.#"))));
        if (Math.Abs(X(cards[ProviderKind.OpenAI].View) - X(quotas.Values.First().Box)) > 0.5)
            throw new InvalidOperationException("Provider cards and quota boxes are not aligned.");

        // With room, the quota area fits its boxes exactly: no scrolling and no gap below them.
        CGSize size = popover.ContentSize;
        nfloat rowsHeight = (nfloat)Math.Ceiling(quotaStack.FittingSize.Height);
        if (rowsHeight <= MaximumQuotaHeight && size.Height < availableHeight &&
            Math.Abs(quotaScroll.Frame.Height - rowsHeight) > 0.5)
            throw new InvalidOperationException($"Quota area {quotaScroll.Frame.Height}pt does not fit its {rowsHeight}pt of boxes.");
        if (size.Width != PanelWidth || size.Height > availableHeight)
            throw new InvalidOperationException($"Popover {size.Width}x{size.Height} does not fit {PanelWidth}x{availableHeight}.");
        NSView[] rows = root.ArrangedSubviews;
        for (int i = 0; i < rows.Length; i++)
        {
            CGRect frame = rows[i].Frame;
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 ||
                frame.Right > root.Bounds.Width + 0.5 || frame.Bottom > root.Bounds.Height + 0.5)
                throw new InvalidOperationException("A popover row is outside the visible content.");
            for (int j = i + 1; j < rows.Length; j++)
                if (frame.IntersectsWith(rows[j].Frame))
                    throw new InvalidOperationException("Popover rows overlap.");
        }
        return new(size, titleX, boxX, availableHeight, ContentWidth - CardPadding * 2);
    }

    // A label's frame reaches about 2pt past where its text (alignment rect) starts and ends; the link may end that far
    // past the card's inner edge (the heading before it is measured the same way), as long as it stays inside the card.
    private const int LabelFramePadding = 3;

    // Only the xAI card has the usage link: right of its heading, inside the card, and its own VoiceOver link element.
    private void VerifyUsageLink(ProviderKind provider, Card card)
    {
        if ((provider == ProviderKind.Xai) != (card.Link is not null) || (card.Link is null) != (card.View.LinkRect is null))
            throw new InvalidOperationException($"{provider} card has the wrong usage-link setup (only xAI has one).");
        if (card.Link is not { } link) return;
        CGRect inCard = card.View.LinkRect!.Value;
        CGRect heading = card.Heading.ConvertRectToView(card.Heading.Bounds, card.View);
        if (link.ToolTip != ProviderStatusPages.XaiUsageLabel + ": " + ProviderStatusPages.XaiUsage.AbsoluteUri ||
            link.AttributedStringValue.Value != ProviderStatusPages.XaiUsageLabel ||
            !link.AccessibilityElement || link.AccessibilityRole != NSAccessibilityRoles.LinkRole.ToString() ||
            link.AccessibilityLabel != ProviderStatusPages.XaiUsageLabel || link.AccessibilityHelp != UsageLink.AccessibleHelp)
            throw new InvalidOperationException("The xAI usage link text, tooltip or accessibility description is wrong.");
        if (inCard.Width <= 0 || inCard.Height <= 0 || inCard.X < heading.Right - 0.5 || inCard.Right > card.View.Bounds.Width - CardPadding + LabelFramePadding ||
            inCard.Y < 0 || inCard.Bottom > card.View.Bounds.Height || inCard.IntersectsWith(heading))
            throw new InvalidOperationException($"The xAI usage link overlaps the heading or leaves the card (link {inCard}, heading {heading}, card {card.View.Bounds}).");
        // Its label is not clipped.
        if (link.AttributedStringValue.Size.Width > link.Frame.Width + 0.5)
            throw new InvalidOperationException("The xAI usage link text is cut off.");
    }

    // Smoke: route a click at the center of the xAI link, or at a point of the card that is not the link
    // (the card's top-left corner area), the way the card's click recognizer does.
    internal void ClickCard(ProviderKind provider, bool onLink)
    {
        CardView view = cards[provider].View;
        CGRect? link = view.LinkRect;
        view.Route(onLink && link is { } rect ? new CGPoint(rect.GetMidX(), rect.GetMidY())
            : new CGPoint(CardPadding + 2, view.Bounds.Height - CardPadding));
    }

    // Smoke: the xAI link as VoiceOver would press it.
    internal bool PressUsageLink() => cards[ProviderKind.Xai].Link?.AccessibilityPerformPress() ?? false;

    // Smoke: the popover content as a PNG, only to save synthetic review images when a smoke run is asked to.
    internal NSData? SnapshotPng()
    {
        content.LayoutSubtreeIfNeeded();
        NSBitmapImageRep? bitmap = content.BitmapImageRepForCachingDisplayInRect(content.Bounds);
        if (bitmap is null) return null;
        content.CacheDisplay(content.Bounds, bitmap);
        return bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
    }

    // Smoke: for the layout last fitted, scroll the panel the way a user would and report the first control that cannot be
    // brought fully into view (null when every card, the xAI link, the quota area and the controls can).
    internal (nfloat Document, nfloat Visible, bool Scrolls, string? Unreachable) ScrollAudit()
    {
        NSClipView clip = SettledClip();
        nfloat document = page.Frame.Height, visible = clip.Bounds.Height;
        if (Math.Abs(visible - popover.ContentSize.Height) > 0.5)
            return (document, visible, document > visible + 0.5, $"popover size ({popover.ContentSize.Height}pt) not applied to the scroll view ({visible}pt)");
        nfloat maximum = (nfloat)Math.Max(0, document - visible);
        var targets = new List<(string Name, NSView View)> { ("title", title) };
        foreach (var (provider, card) in cards)
        {
            targets.Add((provider + " card", card.View));
            if (card.Link is not null) targets.Add((provider + " usage link", card.Link));
        }
        targets.AddRange([("quota title", quotaTitle), ("quota area", quotaScroll), ("check times", checkedCaption),
            ("Refresh", refresh), ("feedback", feedback), ("holiday option", holiday), ("auto start option", autoStart)]);
        string? unreachable = null;
        foreach (var (name, view) in targets)
        {
            CGRect inPage = view.ConvertRectToView(view.Bounds, page);
            clip.ScrollToPoint(new CGPoint(0, Math.Clamp(inPage.Y, 0, maximum)));
            pageScroll.ReflectScrolledClipView(clip);
            CGRect seen = view.ConvertRectToView(view.Bounds, clip);
            if (seen.Y < clip.Bounds.Y - 0.5 || seen.Bottom > clip.Bounds.Bottom + 0.5) unreachable ??= name;
        }
        clip.ScrollToPoint(CGPoint.Empty);
        pageScroll.ReflectScrolledClipView(clip);
        return (document, visible, document > visible + 0.5, unreachable);
    }

    // A shown popover may apply a new ContentSize a moment later; give it a few run-loop turns before the scroll view is read.
    private NSClipView SettledClip()
    {
        content.LayoutSubtreeIfNeeded();
        NSClipView clip = pageScroll.ContentView;
        for (int turn = 0; turn < 10 && Math.Abs(clip.Bounds.Height - popover.ContentSize.Height) > 0.5; turn++)
        {
            NSRunLoop.Main.RunUntil(NSRunLoopMode.Default, NSDate.FromTimeIntervalSinceNow(0.05));
            content.LayoutSubtreeIfNeeded();
        }
        return clip;
    }

    // Smoke: scroll the panel to its top or bottom (review pictures of a short screen).
    internal void ScrollPanel(bool toBottom)
    {
        NSClipView clip = SettledClip();
        nfloat maximum = (nfloat)Math.Max(0, page.Frame.Height - clip.Bounds.Height);
        clip.ScrollToPoint(new CGPoint(0, toBottom ? maximum : 0));
        pageScroll.ReflectScrolledClipView(clip);
    }

    // Smoke: the visible quota area height (shrinks and scrolls on a short screen).
    internal nfloat QuotaAreaHeight => quotaScroll.Frame.Height;
    internal nfloat QuotaRowsHeight => (nfloat)Math.Ceiling(quotaStack.FittingSize.Height);

    // Smoke: press a card as VoiceOver or a click would.
    internal bool PressCard(ProviderKind provider) => cards[provider].View.AccessibilityPerformPress();

    // The shared row text (name and value together, as before the split), not what is painted.
    internal string QuotaRowText(QuotaProvider provider, string windowId) => quotas[provider].Rows[windowId].FullText;

    // Smoke: what the row paints: the (possibly shortened) name, the value, and how wide each field is.
    internal (string Name, string Value, double NameWidth, double ValueWidth, bool ValueFits) QuotaRowParts(QuotaProvider provider, string windowId)
    {
        QuotaRowView row = quotas[provider].Rows[windowId];
        return (row.VisibleName, row.VisibleValue, row.NameWidth, row.ValueWidth, row.ValueFits);
    }

    // Smoke: spoken value of a row and the time layer of its bar.
    internal string QuotaRowSpoken(QuotaProvider provider, string windowId) =>
        quotas[provider].Bars.TryGetValue(windowId, out UsageBarView? bar) ? bar.AccessibilityLabel ?? ""
            : quotas[provider].Rows[windowId].FullText;
    internal (double? Elapsed, nfloat MarkerLeft, nfloat MarkerWidth, nfloat ElapsedWidth) QuotaBarTime(QuotaProvider provider, string windowId)
    {
        UsageBarView bar = quotas[provider].Bars[windowId];
        return (bar.ElapsedPercent, bar.MarkerLeft, bar.MarkerWidth, bar.ElapsedWidth);
    }

    // Smoke: how many usage bars are drawn, and the visible fill of one of them.
    internal int QuotaBarCount => quotas.Values.Sum(views => views.Bars.Count);

    // Smoke: what the panel really holds per quota box (its row IDs, the box view, how many boxes), to compare with the model.
    internal string[] QuotaRowIds(QuotaProvider provider) => quotas[provider].Rows.Keys.Order(StringComparer.Ordinal).ToArray();
    internal NSView QuotaBoxView(QuotaProvider provider) => quotas[provider].Box;
    internal int QuotaBoxCount => quotas.Count;
    internal nfloat QuotaBarFill(QuotaProvider provider, string windowId) => quotas[provider].Bars[windowId].FilledWidth;

    public void Dispose()
    {
        Close();
        popover.Dispose();
        foreach (Card card in cards.Values) card.Menu.Dispose();
    }
}
