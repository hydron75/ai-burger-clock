using System.Globalization;

namespace AiBurgerClock;

// Golden checks for shared panel text; no UI, network or user data.
internal static class PanelModelTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Panel model: " + label);
        }
        void Same(string actual, string expected, string label) =>
            Check(actual == expected, $"{label}\n  expected: {expected}\n  actual:   {actual}");

        // Day names and time separators follow the current culture, as on Windows; pin it for goldens.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Names(Check, Same);
            Schedule(Check, Same);
            Cards(Check, Same);
            Quotas(Check, Same);
            Cultures(Check, Same);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        return count;
    }

    private static readonly string QuotaPolicyNote =
        "기본 6시간 · 잔여 0% 초과~10% 미만 1시간 · 잔여 0%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도\n공식 CLI 응답 수신 시각이며 서버 데이터 생성 시각을 보장하지 않습니다.";

    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

    private static void Names(Action<bool, string> check, Action<string, string, string> same)
    {
        same(ProviderNames.Provider(ProviderKind.OpenAI), "ChatGPT", "OpenAI is shown as ChatGPT");
        same(ProviderNames.Provider(ProviderKind.Claude), "Claude", "Claude name");
        same(ProviderNames.Provider(ProviderKind.Gemini), "Gemini", "Gemini name");
        same(ProviderNames.Provider("OpenAI"), "ChatGPT", "stored provider value is shown as ChatGPT");
        same(ProviderNames.Provider("Unlisted"), "Unlisted", "unknown stored value is shown unchanged");
        same(ProviderNames.Quota(QuotaProvider.Codex), "ChatGPT", "Codex quota heading");
        same(ProviderNames.Quota(QuotaProvider.Claude), "Claude", "Claude quota heading");
        same(ProviderNames.Quota(QuotaProvider.Gemini), "Gemini", "Gemini quota heading");
        check(QuotaNames.For(QuotaProvider.Codex) == "Work / Codex", "quota identity names stay unchanged");
    }

    private static void Schedule(Action<bool, string> check, Action<string, string, string> same)
    {
        check(StatusPanelModel.Tone(AgentState.FullThrottle) == PanelTone.Good &&
            StatusPanelModel.Tone(AgentState.BurgerTime) == PanelTone.Danger, "state tones");
        same(StatusPanelModel.Title, "AI AGENT TRAFFIC", "panel title");
        same(StatusPanelModel.WaitingCaption, "공식 상태 갱신 대기", "caption before the first poll");
        same(StatusPanelModel.HolidayOption, "미국 연방 공휴일 보정", "holiday option");

        // Weekday FULL THROTTLE: Tue 14:00 KST, US standard business hours not started.
        var full = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-06-16T05:00:00Z")));
        same(full.State, "●  FULL THROTTLE", "FULL state line");
        check(full.StateTone == PanelTone.Good, "FULL tone");
        same(full.Countdown, "전환까지  08:00:00", "FULL countdown");
        same(full.Next, "다음: Tue 22:00 KST", "weekday next transition has no suffix");
        same(full.NextDetail, "다음 전환: 2026-06-16 22:00:00 KST\n공휴일 보정: 꺼짐\n연장에 반영된 공휴일: \n주말 여부와 공휴일 연장은 독립적으로 기록됩니다.",
            "next transition detail");
        same(full.TimeZone, "US: DST · ET UTC-4 / PT UTC-7", "summer time zone line");
        same(full.TimeZoneDetail, "Eastern: 2026-06-16 01:00 -04:00\nPacific: 2026-06-15 22:00 -07:00\n정책: us-business-et09-pt18-v1",
            "time zone detail");

        var burger = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-06-16T14:00:00Z")));
        same(burger.State, "●  BURGER TIME", "BURGER state line");
        check(burger.StateTone == PanelTone.Danger, "BURGER tone");
        same(burger.Countdown, "전환까지  11:00:00", "BURGER countdown");
        same(burger.Next, "다음: Wed 10:00 KST", "BURGER next transition");

        var weekend = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-06-20T05:00:00Z")));
        same(weekend.Countdown, "전환까지  56:00:00", "weekend countdown over 24 hours");
        same(weekend.Next, "다음: Mon 22:00 KST · Weekend", "weekend suffix");

        var holiday = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-11-26T17:00:00Z"), adjustForHolidays: true));
        same(holiday.Next, "다음: Fri 23:00 KST · 공휴일", "weekday holiday suffix");
        same(holiday.NextDetail, "다음 전환: 2026-11-27 23:00:00 KST\n공휴일 보정: 켜짐\n연장에 반영된 공휴일: Thanksgiving Day\n주말 여부와 공휴일 연장은 독립적으로 기록됩니다.",
            "holiday detail names the holiday");
        same(holiday.TimeZone, "US: Standard · ET UTC-5 / PT UTC-8", "winter time zone line");
        check(holiday.TimeZoneDetail.EndsWith("정책: us-business-et09-pt18-holidays-v2", StringComparison.Ordinal), "holiday policy version");

        // Labor Day weekend: both flags are shown, unlike the old macOS window that showed only the holiday.
        var both = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-09-05T15:00:00Z"), adjustForHolidays: true));
        same(both.Next, "다음: Tue 22:00 KST · 주말+공휴일", "weekend and holiday suffix");
        same(both.Countdown, "전환까지  70:00:00", "long weekend countdown");
        var notAdjusted = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-09-07T15:00:00Z")));
        same(notAdjusted.State, "●  BURGER TIME", "holiday adjustment off keeps Labor Day as a business day");
        same(notAdjusted.Next, "다음: Tue 10:00 KST", "no suffix while adjustment is off");

        var mixed = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-11-01T08:00:00Z")));
        same(mixed.TimeZone, "US: Mixed DST · ET UTC-5 / PT UTC-7", "fall-back gap between Eastern and Pacific");
        var spring = StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-03-08T08:00:00Z")));
        same(spring.TimeZone, "US: Mixed DST · ET UTC-4 / PT UTC-8", "spring-forward gap between Eastern and Pacific");
    }

    private static void Cards(Action<bool, string> check, Action<string, string, string> same)
    {
        var expectedTones = new Dictionary<Recommendation, PanelTone>
        {
            [Recommendation.Go] = PanelTone.Good,
            [Recommendation.Hold] = PanelTone.Caution,
            [Recommendation.Stop] = PanelTone.Danger,
            [Recommendation.Check] = PanelTone.Muted,
            [Recommendation.BurgerTime] = PanelTone.Caution,
            [Recommendation.BurgerServiceIssue] = PanelTone.Danger,
            [Recommendation.BurgerCheck] = PanelTone.Muted
        };
        check(expectedTones.Count == Enum.GetValues<Recommendation>().Length, "every recommendation has an expected tone");
        foreach (var (recommendation, tone) in expectedTones)
            check(StatusPanelModel.Tone(recommendation) == tone, "recommendation tone " + recommendation);

        foreach (var schedule in Enum.GetValues<AgentState>())
        foreach (var provider in Enum.GetValues<ProviderKind>())
        foreach (var official in Enum.GetValues<OfficialStatus>())
        {
            var card = StatusPanelModel.Card(new ProviderStatus(provider, official, At("2026-06-16T05:00:00Z"), null, "reason"), schedule);
            var recommendation = RecommendationPolicy.Calculate(schedule, official);
            string context = $"{schedule}/{provider}/{official}";
            check(card.Provider == provider && card.Recommendation == recommendation, "card identity " + context);
            same(card.Heading, ProviderNames.Provider(provider) + "   " + RecommendationPolicy.Label(recommendation), "heading " + context);
            check(card.HeadingTone == expectedTones[recommendation], "heading tone " + context);
            same(card.Official, "Official: " + RecommendationPolicy.OfficialLabel(official), "official line " + context);
        }

        var degraded = StatusPanelModel.Card(new ProviderStatus(ProviderKind.OpenAI, OfficialStatus.Degraded,
            At("2026-06-16T05:00:10Z"), At("2026-06-16T04:55:00Z"), "Codex 성능 저하", "Codex", "inc-1", "Elevated errors",
            "https://status.openai.com", OfficialStatus.Operational), AgentState.FullThrottle);
        same(degraded.Heading, "ChatGPT   HOLD", "degraded ChatGPT heading");
        check(degraded.HeadingTone == PanelTone.Caution, "HOLD tone");
        same(degraded.Official, "Official: 성능 저하", "degraded official line");
        same(degraded.Reason, "Codex 성능 저하", "reason line");
        same(degraded.Detail, "클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록\nCodex 성능 저하\n최근 조회 시도: 06-16 14:00:10 KST\n" +
            "마지막 상태 확인 성공: 06-16 13:55:00 KST\n관련: Codex\n사건: Elevated errors\n사건 ID: inc-1\n마지막 알려진 상태: Operational\nhttps://status.openai.com",
            "full card detail");

        var unknown = StatusPanelModel.Card(ProviderStatus.Unknown(ProviderKind.Gemini), AgentState.BurgerTime);
        same(unknown.Heading, "Gemini   BURGER + CHECK", "unchecked heading during BURGER TIME");
        same(unknown.Official, "Official: UNKNOWN · 확인 불가", "unchecked official line");
        same(unknown.Detail, "클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록\n아직 확인하지 않음\n최근 조회 시도: 없음\n" +
            "마지막 상태 확인 성공: 없음", "unchecked card detail has no empty optional fields");

        // Only the supplied optional fields appear; blank values never leave a bare label.
        var partial = StatusPanelModel.Card(new ProviderStatus(ProviderKind.Claude, OfficialStatus.Operational,
            At("2026-06-16T05:00:10Z"), At("2026-06-16T05:00:10Z"), "관련 서비스 정상", "Claude API", "", "  ",
            "https://status.claude.com"), AgentState.FullThrottle);
        same(partial.Detail, "클릭: 공식 상태 페이지 열기 · 우클릭: 사용 경험 기록\n관련 서비스 정상\n최근 조회 시도: 06-16 14:00:10 KST\n" +
            "마지막 상태 확인 성공: 06-16 14:00:10 KST\n관련: Claude API\nhttps://status.claude.com", "partial optional fields");
        var lastKnownOnly = StatusPanelModel.Card(ProviderStatus.Unknown(ProviderKind.OpenAI) with
            { Status = OfficialStatus.Stale, LastKnownStatus = OfficialStatus.Degraded }, AgentState.FullThrottle);
        check(lastKnownOnly.Detail.EndsWith("\n마지막 알려진 상태: Degraded", StringComparison.Ordinal) &&
            !lastKnownOnly.Detail.Contains("관련:", StringComparison.Ordinal), "last known status alone");

        var states = new[] { ProviderStatus.Unknown(ProviderKind.Gemini), ProviderStatus.Unknown(ProviderKind.OpenAI) };
        var cards = StatusPanelModel.Cards(states, AgentState.FullThrottle);
        check(cards.Select(c => c.Provider).SequenceEqual([ProviderKind.Gemini, ProviderKind.OpenAI]), "cards keep the monitor's order");

        same(StatusPanelModel.CheckedCaption([], false, null), "최근 조회 시도: —\n다음 조회: —", "caption with no providers");
        same(StatusPanelModel.CheckedCaption(states, false, null), "최근 조회 시도: —\n다음 조회: —", "caption before any attempt");
        var checkedStates = new[]
        {
            new ProviderStatus(ProviderKind.OpenAI, OfficialStatus.Operational, At("2026-06-16T05:00:10Z"), null, ""),
            new ProviderStatus(ProviderKind.Claude, OfficialStatus.Operational, At("2026-06-16T05:00:12Z"), null, ""),
            ProviderStatus.Unknown(ProviderKind.Gemini)
        };
        same(StatusPanelModel.CheckedCaption(checkedStates, false, At("2026-06-16T05:05:12Z")),
            "최근 조회 시도: 14:00:12 KST\n다음 조회: 14:05:12 KST", "caption uses the latest attempt and next check");
        same(StatusPanelModel.CheckedCaption(checkedStates, true, At("2026-06-16T05:05:12Z")),
            "최근 조회 시도: 14:00:12 KST\n공식 상태 확인 중…", "refresh in progress replaces the next check");
    }

    private static void Quotas(Action<bool, string> check, Action<string, string, string> same)
    {
        var now = At("2026-10-03T06:00:00Z"); // 15:00 KST
        same(QuotaPanelModel.ShowQuotas, "한도 보기", "toggle to quotas");
        same(QuotaPanelModel.ShowStatus, "상태 보기", "toggle back to status");
        same(QuotaPanelModel.Caption, "색 막대 = 사용한 비율 · 시각은 KST\n마우스 올리기: 상세 · Refresh: 다시 조회", "quota caption");

        var codex = new QuotaState(QuotaProvider.Codex,
            new QuotaReading(QuotaProvider.Codex,
            [
                new QuotaWindow("primary", "5시간", 45, now.AddMinutes(90)),
                new QuotaWindow("secondary", "주간", 92.5, now.AddDays(3).AddHours(4))
            ]),
            CheckedAtUtc: At("2026-10-03T05:55:00Z"), LastSuccessfulCheckUtc: At("2026-10-03T05:55:00Z"),
            NextCheckUtc: At("2026-10-03T11:55:00Z"));
        string codexNote = "ChatGPT (Work/Codex) · 모든 시각 KST\n" + QuotaPanelModel.CodexScopeDetail +
            "\n리셋·한도 복원 예정\n5시간: 2026-10-03 16:30:00 KST\n주간: 2026-10-06 19:00:00 KST\n실제 복원 여부는 새 조회로 확인합니다.\n\n" +
            "마지막 성공: 10-03 14:55\n다음 조회: 10-03 20:55\n최근 시도: 10-03 14:55:00\n" + QuotaPolicyNote;
        var section = QuotaPanelModel.Section(codex, now);
        check(section.Provider == QuotaProvider.Codex, "section provider");
        same(section.Heading.Text, "ChatGPT", "fresh Codex heading");
        check(section.Heading.Tone == PanelTone.Normal, "heading tone");
        same(section.Heading.Detail, codexNote, "Codex note");
        check(section.Scope is { Text: "Work/Codex", Tone: PanelTone.Muted } &&
            section.Scope.Detail == codexNote, "Codex scope line shares the Provider detail");
        check(section.Rows.Select(r => r.WindowId).SequenceEqual(["primary", "secondary"]), "one row per quota window in order");
        same(section.Rows[0].Text, "5시간  45% 사용 · 약 1시간 30분 후 리셋", "fresh row");
        check(section.Rows[0].Tone == PanelTone.Good, "plenty remaining tone");
        same(section.Rows[0].Detail, codexNote, "fresh row shares Provider detail");
        same(section.Rows[1].Text, "주간  92.5% 사용 · 약 3일 4시간 후 리셋", "low weekly row");
        check(section.Rows[1].Tone == PanelTone.Caution, "under 10% tone");
        same(section.Metadata.Text, "성공 10-03 14:55 · 다음 10-03 20:55", "metadata line");
        check(section.Metadata.Tone == PanelTone.Muted && section.Metadata.Detail == codexNote, "metadata tone and detail");

        var claude = new QuotaState(QuotaProvider.Claude,
            new QuotaReading(QuotaProvider.Claude,
            [
                new QuotaWindow("session", "세션", 100, now.AddMinutes(10)),
                new QuotaWindow("weekly", "주간 전체", 12.34, now.AddMinutes(-1)),
                new QuotaWindow("opus", "주간 Opus", 50, null)
            ]),
            LastSuccessfulCheckUtc: At("2026-10-03T05:00:00Z"), NextCheckUtc: At("2026-10-03T06:05:00Z"),
            CacheError: "캐시 저장 실패");
        var claudeSection = QuotaPanelModel.Section(claude, now);
        check(claudeSection.Scope is null, "Claude has no scope line");
        same(claudeSection.Heading.Text, "Claude", "fresh Claude heading");
        same(claudeSection.Rows[0].Text, "세션  100% 사용 · 약 0시간 10분 후 리셋", "exhausted row");
        check(claudeSection.Rows[0].Tone == PanelTone.Danger, "exhausted tone");
        same(claudeSection.Rows[1].Text, "주간 전체  12.3% 사용 (이전) · 갱신 대기", "elapsed reset is shown as previous");
        check(claudeSection.Rows[1].Tone == PanelTone.Muted, "elapsed reset tone");
        string claudeNote = "Claude · 모든 시각 KST\n리셋·한도 복원 예정\n세션: 2026-10-03 15:10:00 KST\n" +
            "주간 전체: 2026-10-03 14:59:00 KST · 예정 시각 경과, 새 조회 필요\n주간 Opus: 제공되지 않음\n실제 복원 여부는 새 조회로 확인합니다.\n\n" +
            "마지막 성공: 10-03 14:00\n다음 조회: 10-03 15:05\n최근 시도: —\n캐시 저장 실패\n" + QuotaPolicyNote;
        same(claudeSection.Rows[1].Detail, claudeNote, "elapsed row shares qualified Provider detail");
        same(claudeSection.Rows[2].Text, "주간 Opus  50% 사용 · 리셋 미제공", "missing reset row");
        same(claudeSection.Rows[2].Detail, claudeNote, "missing reset shares Provider detail");
        check(claudeSection.Metadata.Tone == PanelTone.Danger, "cache error highlights metadata");

        var previous = QuotaPanelModel.Section(claude with { IsPrevious = true, CacheError = "" }, now);
        same(previous.Heading.Text, "Claude · 이전 조회값", "previous values heading");
        same(previous.Rows[2].Text, "주간 Opus  50% 사용 (이전) · 리셋 미제공", "previous row");
        check(previous.Rows.All(r => r.Tone == PanelTone.Muted), "previous rows are muted");
        same(QuotaPanelModel.Section(claude with { IsPrevious = true, IsRefreshing = true }, now).Heading.Text, "Claude · 확인 중",
            "refreshing takes precedence over previous");

        var waiting = QuotaPanelModel.Section(new QuotaState(QuotaProvider.Codex), now);
        same(waiting.Heading.Text, "ChatGPT", "no reading heading");
        check(waiting.Rows.Count == 1 && waiting.Rows[0].WindowId == "" && waiting.Rows[0].Tone == PanelTone.Muted, "placeholder row");
        same(waiting.Rows[0].Text, "한도 조회 대기 · 공식 CLI 로그인 필요", "placeholder text");
        same(waiting.Metadata.Text, "성공 — · 다음 —", "metadata before any check");
        var failed = QuotaPanelModel.Section(new QuotaState(QuotaProvider.Claude, IsPrevious: true, Error: "공식 CLI를 찾지 못했습니다."), now);
        same(failed.Heading.Text, "Claude", "previous without a reading has no suffix");
        same(failed.Rows[0].Text, "공식 CLI를 찾지 못했습니다.", "error replaces the placeholder");

        foreach ((double used, PanelTone tone) in new[] { (0.0, PanelTone.Good), (90.0, PanelTone.Good), (90.1, PanelTone.Caution),
            (99.9, PanelTone.Caution), (100.0, PanelTone.Danger), (101.0, PanelTone.Danger) })
            check(QuotaPanelModel.Tone(new QuotaWindow("w", "w", used, null), false, now) == tone, $"remaining tone at {used}% used");
        check(QuotaPanelModel.Tone(new QuotaWindow("w", "w", 100, null), true, now) == PanelTone.Muted, "previous wins over exhausted");
        check(QuotaPanelModel.Tone(new QuotaWindow("w", "w", 100, now), false, now) == PanelTone.Muted, "reset at now counts as elapsed");

        var sections = QuotaPanelModel.Sections([claude, codex], now);
        check(sections.Select(s => s.Provider).SequenceEqual([QuotaProvider.Claude, QuotaProvider.Codex]), "sections keep the monitor's order");

        var gemini = new QuotaState(QuotaProvider.Gemini, new(QuotaProvider.Gemini,
            [new("gemini-5h", "5시간", 2.012, now.AddHours(2), 300),
             new("gemini-weekly", "주간", 0.335, now.AddDays(6), 10080)]),
            CheckedAtUtc: now, LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(6));
        var geminiSection = QuotaPanelModel.Section(gemini, now);
        same(geminiSection.Heading.Text, "Gemini", "Gemini keeps its product heading");
        same(geminiSection.Scope!.Text, "Antigravity · Gemini 모델", "Gemini quota scope is explicit");
        check(geminiSection.Scope.Tone == PanelTone.Muted && geminiSection.Scope.Detail.Contains("Gemini Apps") &&
            geminiSection.Scope.Detail.Contains("Claude/GPT"), "Gemini scope does not imply web Apps or third-party subscription limits");
        same(geminiSection.Rows[0].Text, "5시간  2% 사용 · 약 2시간 0분 후 리셋", "Gemini five-hour countdown");
        same(geminiSection.Rows[1].Text, "주간  0.3% 사용 · 약 6일 0시간 후 리셋", "Gemini weekly fraction is not the rounded TSV value");
        check(geminiSection.Rows.All(row => row.Tone == PanelTone.Good), "Gemini uses existing quota tones");
        check(geminiSection.Rows[0].Detail.Contains("Gemini (Antigravity)") &&
            geminiSection.Rows[0].Detail.Contains("서버 데이터 생성 시각을 보장하지 않습니다"), "Gemini details preserve freshness qualification");
        same(QuotaPanelModel.Section(new QuotaState(QuotaProvider.Gemini), now).Rows[0].Text,
            "한도 조회 대기 · agy CLI 로그인 필요", "Gemini placeholder identifies agy");
        var oldGemini = QuotaPanelModel.Section(gemini with { IsPrevious = true }, now);
        same(oldGemini.Heading.Text, "Gemini · 이전 조회값", "failed Gemini quota is explicitly previous");
        check(oldGemini.Rows.All(row => row.Tone == PanelTone.Muted) && oldGemini.Scope!.Text == geminiSection.Scope.Text,
            "previous Gemini values retain scope and muted tones");
        check(QuotaPanelModel.Sections([codex, claude, gemini], now).Count == 3,
            "all three quota sections are available without replacing health data");
    }

    // Hosts run in the user's culture: day names follow it, percentages never do.
    private static void Cultures(Action<bool, string> check, Action<string, string, string> same)
    {
        var korean = TryCulture("ko-KR");
        if (korean is not null)
        {
            CultureInfo.CurrentCulture = korean;
            same(StatusPanelModel.Schedule(AgentSchedule.GetSnapshot(At("2026-11-26T17:00:00Z"), adjustForHolidays: true)).Next,
                "다음: 금 23:00 KST · 공휴일", "Korean day name as on a Korean Windows install");
        }
        var german = TryCulture("de-DE");
        if (german is not null)
        {
            CultureInfo.CurrentCulture = german;
            var now = At("2026-10-03T06:00:00Z");
            var row = QuotaPanelModel.Section(new QuotaState(QuotaProvider.Claude,
                new QuotaReading(QuotaProvider.Claude, [new QuotaWindow("w", "주간", 12.34, null)])), now).Rows[0];
            check(row.Text.StartsWith("주간  12.3% 사용", StringComparison.Ordinal) && row.UsedPercent == 12.34,
                "percent text uses a dot in every culture");
        }
    }

    private static CultureInfo? TryCulture(string name)
    {
        try { return CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { return null; } // Invariant-globalization runtimes.
    }
}
