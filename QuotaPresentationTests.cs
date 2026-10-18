using System.Globalization;

namespace AiBurgerClock;

// Pure presentation checks formerly limited to Windows smoke, plus shared Provider detail coverage.
internal static class QuotaPresentationTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Quota presentation: " + label);
        }
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        string Reset(DateTimeOffset? at, int? minutes = null) => DisplayFormatting.CompactResetCountdown(at, now, minutes);
        Check(Reset(null) == "리셋 미제공", "missing reset");
        Check(Reset(now) == "갱신 대기", "elapsed reset never assumes recovery");
        Check(Reset(now.AddSeconds(59)) == "곧 리셋 예정", "sub-minute reset");
        Check(Reset(now.AddMinutes(59), 300) == "약 59분 후 리셋", "session below one hour");
        Check(Reset(now.AddHours(1), 300) == "약 1시간 후 리셋", "session hour boundary");
        Check(Reset(now.AddHours(2).AddMinutes(37), 300) == "약 2시간 37분 후 리셋", "session hours/minutes");
        Check(Reset(now.AddDays(1), 10080) == "약 1일 후 리셋", "weekly day boundary");
        Check(Reset(now.AddDays(3).AddHours(4), 10080) == "약 3일 4시간 후 리셋", "weekly days/hours");
        Check(Reset(now.AddHours(7), 10080) == "약 7시간 후 리셋", "weekly below one day");
        Check(Reset(now.AddMinutes(25), 10080) == "약 1시간 후 리셋", "weekly short time rounds up");
        Check(Reset(now.AddMinutes(119).AddSeconds(1), 300) == "약 2시간 후 리셋", "minute carry");
        Check(Reset(now.AddHours(23).AddSeconds(1), 10080) == "약 1일 후 리셋", "hour carry");
        Check(Reset(now.AddHours(25), 300) == "약 25시간 후 리셋", "session window kind wins over duration");
        Check(Reset(now.AddDays(2).AddHours(3)) == "약 2일 3시간 후 리셋", "unknown window fallback");

        Check(Reset(now.AddMinutes(15), 300) == "약 15분 후 리셋", "session omits zero leading hours");
        Check(Reset(now.AddMinutes(1), 300) == "약 1분 후 리셋", "exact minute begins rounded display");
        Check(Reset(now.AddMinutes(1).AddSeconds(1), 300) == "약 2분 후 리셋", "partial minute still rounds up");
        Check(Reset(now.AddMinutes(59).AddSeconds(30), 300) == "약 1시간 후 리셋", "session carry omits zero trailing minutes");
        Check(Reset(now.AddHours(1).AddSeconds(1), 300) == "약 1시간 1분 후 리셋", "session just after exact hour");
        Check(Reset(now.AddMinutes(59).AddSeconds(30), 10080) == "약 1시간 후 리셋", "weekly hour rounding omits zero days");
        Check(Reset(now.AddHours(1), 10080) == "약 1시간 후 리셋", "weekly exact hour omits zero days");
        Check(Reset(now.AddHours(1).AddSeconds(1), 10080) == "약 2시간 후 리셋", "weekly just after hour rounds up");
        Check(Reset(now.AddHours(23).AddMinutes(59).AddSeconds(30), 10080) == "약 1일 후 리셋", "weekly carry omits zero trailing hours");
        Check(Reset(now.AddDays(1).AddSeconds(1), 10080) == "약 1일 1시간 후 리셋", "weekly just after exact day");
        Check(Reset(now.AddMinutes(15)) == "약 15분 후 리셋", "unknown short window omits zero hours");
        Check(Reset(now.AddMinutes(59).AddSeconds(30)) == "약 1시간 후 리셋", "unknown window minute carry");
        Check(Reset(now.AddHours(23).AddMinutes(59).AddSeconds(30)) == "약 24시간 후 리셋", "unknown window stays minute-based below one day");
        Check(Reset(now.AddDays(1)) == "약 1일 후 리셋", "unknown exact day selects day units");
        Check(Reset(now.AddHours(5), 300) == "약 5시간 후 리셋", "session exact five hours omits zero minutes");
        Check(Reset(now.AddDays(2), 10080) == "약 2일 후 리셋", "weekly exact two days omits zero hours");
        Check(Reset(now.AddHours(4).AddMinutes(59).AddSeconds(30), 300) == "약 5시간 후 리셋", "session carry to five hours omits zero minutes");
        Check(Reset(now.AddHours(47).AddMinutes(59).AddSeconds(30), 10080) == "약 2일 후 리셋", "weekly carry to two days omits zero hours");

        Check(QuotaPanelModel.AccessibleName == "개인 계정 사용량", "shared usage title");
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            var state = new QuotaState(provider, new(provider,
                [new("session", "5시간", 12.34, now.AddMinutes(25), 300),
                 new("weekly", "주간", 92, now.AddHours(7), 10080),
                 new("missing", "모델별", 0, null)]),
                CheckedAtUtc: now, LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(6));
            var section = QuotaPanelModel.Section(state, now);
            Check(section.Rows[0].Text == "5시간  12.3% 사용 · 약 25분 후 리셋", provider + " shared used row");
            Check(section.Rows[1].Text == "주간  92% 사용 · 약 7시간 후 리셋", provider + " weekly units");
            Check(section.Rows[0].UsedPercent == 12.34 && section.Rows[2].UsedPercent == 0, provider + " precise observed bar data");
            Check(section.Rows[0].Tone == PanelTone.Good && section.Rows[1].Tone == PanelTone.Caution,
                provider + " remaining-based tones unchanged");
            Check(section.Heading.Detail == section.Detail && section.Metadata.Detail == section.Detail &&
                section.Rows.All(row => row.Detail == section.Detail) && (section.Scope is null || section.Scope.Detail == section.Detail),
                provider + " one detail for heading/scope/rows/metadata");
            Check(section.Detail.Contains("5시간: 2026-10-10 21:25:00 KST\n주간: 2026-10-11 04:00:00 KST\n모델별: 제공되지 않음", StringComparison.Ordinal),
                provider + " exact KST reset instants and missing reset");
            Check(!section.Detail.Contains("12.3%", StringComparison.Ordinal) && section.Detail.Contains("실제 복원 여부는 새 조회로 확인합니다.", StringComparison.Ordinal),
                provider + " no duplicated quota percentages or assumed restoration");
            Check(section.Detail.Contains("마지막 성공: 10-10 21:00\n다음 조회: 10-11 03:00\n최근 시도: 10-10 21:00:00", StringComparison.Ordinal) &&
                section.Detail.Contains("서버 데이터 생성 시각을 보장하지 않습니다", StringComparison.Ordinal), provider + " query metadata/freshness preserved");
            Check(section.Detail.Contains("기본 1시간 · 사용 90% 초과~100% 미만은 1시간 · 사용 100%는 15분 · 리셋 전후 15분은 5분 · 실패 시 15분부터 재시도", StringComparison.Ordinal) &&
                !section.Detail.Contains("잔여", StringComparison.Ordinal), provider + " polling text uses consumed percentage");
            var stale = QuotaPanelModel.Section(state with { IsPrevious = true, Error = "조회 실패", CacheError = "캐시 실패" }, now);
            Check(stale.Rows.All(row => row.Tone == PanelTone.Muted && row.Text.Contains("사용 (이전)", StringComparison.Ordinal)) &&
                stale.Metadata.Tone == PanelTone.Danger && stale.Detail.Contains("조회 실패\n캐시 실패", StringComparison.Ordinal), provider + " previous/error/cache semantics");
            var empty = QuotaPanelModel.Section(new(provider), now);
            Check(empty.Rows.Single().UsedPercent is null && !empty.Detail.Contains("리셋·한도 복원 예정", StringComparison.Ordinal),
                provider + " no reading has no invented bar/reset header");
            Check(empty.Rows.Single().ComparisonText is null && empty.Heading.ComparisonText is null &&
                empty.Metadata.ComparisonText is null && empty.Scope?.ComparisonText is null,
                provider + " placeholder and non-quota lines have no row comparison");
            var noWindows = QuotaPanelModel.Section(state with { Reading = new(provider, []) }, now);
            Check(noWindows.Rows.Count == 0 && !noWindows.Detail.Contains("리셋·한도 복원 예정", StringComparison.Ordinal), provider + " empty windows have no bare header");
            string? scope = provider == QuotaProvider.Codex ? QuotaPanelModel.CodexScopeDetail :
                provider == QuotaProvider.Gemini ? QuotaPanelModel.GeminiScopeDetail : null;
            Check(scope is null || section.Detail.Split(scope, StringSplitOptions.None).Length == 2, provider + " scope appears once");
        }
        var expired = QuotaPanelModel.Section(new(QuotaProvider.Claude, new(QuotaProvider.Claude,
            [new("session", "5시간", 97, now, 300)])), now);
        Check(expired.Rows.Single() is { UsedPercent: 97, Tone: PanelTone.Muted } &&
            expired.Rows.Single().Text == "5시간  97% 사용 (이전) · 갱신 대기" &&
            expired.Detail.Contains("예정 시각 경과, 새 조회 필요", StringComparison.Ordinal), "elapsed value retained, not reset to zero");
        foreach (var provider in Enum.GetValues<QuotaProvider>())
        {
            var state = new QuotaState(provider, new(provider,
                [new("active", "합성 5시간", 55, now.AddHours(3), 300),
                 new("weekly", "합성 주간", 10, now.AddHours(126), 10080),
                 new("inactive", "합성 5시간 미확인", 0, now.AddHours(5), 300)]),
                CheckedAtUtc: now, LastSuccessfulCheckUtc: now, NextCheckUtc: now.AddHours(6));
            var fresh = QuotaPanelModel.Section(state, now);
            var later = QuotaPanelModel.Section(state, now.AddMinutes(17));
            Check(fresh.Rows[0].ElapsedPercent == 40 && fresh.Rows[1].ElapsedPercent == 25 &&
                fresh.Rows[2].ElapsedPercent is null, provider + " optional time layer uses only eligible windows");
            Check(fresh.Rows[0].ComparisonText == "시간 경과 40% · 균등 사용 기준보다 15%p 빠름 · 방금 전 기준" &&
                fresh.Rows[1].ComparisonText == "시간 경과 25% · 균등 사용 기준보다 15%p 느림 · 방금 전 기준" &&
                fresh.Rows[2].ComparisonText is null, provider + " each quota row exposes only its own comparison or null");
            Check(fresh.Rows.Select(row => row.ElapsedPercent).SequenceEqual(later.Rows.Select(row => row.ElapsedPercent)) &&
                fresh.Rows.Select(row => row.UsedPercent).SequenceEqual(later.Rows.Select(row => row.UsedPercent)),
                provider + " time and usage stay pinned to the same successful receipt");
            Check(later.Detail.Contains("마지막 조회 기준, 17분 전", StringComparison.Ordinal) &&
                fresh.Detail.Contains("마지막 조회 기준, 방금 전", StringComparison.Ordinal), provider + " reference age advances without moving the comparison");
            Check(later.Rows[0].ComparisonText == "시간 경과 40% · 균등 사용 기준보다 15%p 빠름 · 17분 전 기준" &&
                later.Rows[1].ComparisonText == "시간 경과 25% · 균등 사용 기준보다 15%p 느림 · 17분 전 기준",
                provider + " row comparison age advances independently without parsing Provider detail");
            Check(later.Detail.Contains("균등 사용 기준보다 15%p 빠름", StringComparison.Ordinal) &&
                later.Detail.Contains("균등 사용 기준보다 15%p 느림", StringComparison.Ordinal), provider + " detail describes both directions in percentage points");
            Check(later.Heading.Detail == later.Detail && later.Rows.All(row => row.Detail == later.Detail) &&
                later.Metadata.Detail == later.Detail && (later.Scope is null || later.Scope.Detail == later.Detail),
                provider + " Provider tooltip remains shared by all lines, separate from row accessibility");
            Check(later.Heading.ComparisonText is null && later.Metadata.ComparisonText is null &&
                later.Scope?.ComparisonText is null && later.Rows.All(row =>
                    row.ComparisonText is null || (!row.ComparisonText.Contains('\n') &&
                    !row.ComparisonText.Contains("리셋·한도 복원", StringComparison.Ordinal))),
                provider + " comparison field never duplicates other rows or whole-Provider tooltip");
            Check(later.Rows.Count == state.Reading!.Windows.Count && !later.Rows[0].Text.Contains("시간 경과", StringComparison.Ordinal) &&
                later.Rows[0].Tone == fresh.Rows[0].Tone && later.Metadata.Text == fresh.Metadata.Text,
                provider + " comparison adds no visible row or policy/tone/metadata change");
            Check(state.NextCheckUtc == now.AddHours(6) && !state.IsRefreshing && state.CheckedAtUtc == now &&
                state.LastSuccessfulCheckUtc == now, provider + " pure presentation changes no query state or due date");
            var previous = QuotaPanelModel.Section(state with { IsPrevious = true }, now.AddMinutes(17));
            Check(previous.Rows.All(row => row.ElapsedPercent is null && row.Tone == PanelTone.Muted) &&
                !previous.Detail.Contains("%p", StringComparison.Ordinal) && previous.Detail.Contains("마지막 조회 기준, 17분 전", StringComparison.Ordinal),
                provider + " previous readings retain their age but remove time reference and pace");
            Check(previous.Rows.All(row => row.ComparisonText is null), provider + " previous values clear every row comparison");
            var inFlight = QuotaPanelModel.Section(state with { IsRefreshing = true, NextCheckUtc = null }, now.AddMinutes(17));
            Check(inFlight.Rows[0].ElapsedPercent == 40 && inFlight.Rows[0].UsedPercent == 55 &&
                inFlight.Detail.Contains("마지막 조회 기준, 17분 전", StringComparison.Ordinal), provider + " an in-flight query never mixes a new time with old usage");
            Check(inFlight.Rows.Select(row => row.ComparisonText).SequenceEqual(later.Rows.Select(row => row.ComparisonText)),
                provider + " in-flight row text retains the last successful comparison");
            var afterReset = QuotaPanelModel.Section(state, now.AddHours(3));
            Check(afterReset.Rows[0].ElapsedPercent is null && afterReset.Rows[0].UsedPercent == 55 &&
                afterReset.Rows[0].Tone == PanelTone.Muted && afterReset.Rows[1].ElapsedPercent == 25,
                provider + " only the expired window loses its comparison without assuming recovery");
            Check(afterReset.Rows[0].ComparisonText is null && afterReset.Rows[1].ComparisonText ==
                "시간 경과 25% · 균등 사용 기준보다 15%p 느림 · 3시간 전 기준", provider + " expiry clears only that row's comparison text");
            var refreshed = QuotaPanelModel.Section(state with { LastSuccessfulCheckUtc = now.AddHours(1) }, now.AddHours(1));
            Check(refreshed.Rows[0].ElapsedPercent == 60 && refreshed.Detail.Contains("균등 사용 기준보다 5%p 느림", StringComparison.Ordinal),
                provider + " a new success updates both the inferred time and detailed comparison");
            Check(refreshed.Rows[0].ComparisonText == "시간 경과 60% · 균등 사용 기준보다 5%p 느림 · 방금 전 기준",
                provider + " new receipt updates row-specific comparison text");
            foreach (var unavailable in new[]
            {
                state with { LastSuccessfulCheckUtc = null },
                state with { LastSuccessfulCheckUtc = now.AddTicks(1) },
                state with { Reading = new(provider, [state.Reading!.Windows[0] with { ResetsAtUtc = null }]) },
                state with { Reading = new(provider, [state.Reading!.Windows[0] with { WindowMinutes = null }]) },
                state with { Reading = new(provider, [state.Reading!.Windows[0] with { ResetsAtUtc = now.AddHours(6) }]) }
            })
                Check(QuotaPanelModel.Section(unavailable, now).Rows.All(row => row.ComparisonText is null),
                    provider + " unavailable comparison is null in the shared row contract");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Check(Reset(now.AddMinutes(25), 300) == "약 25분 후 리셋", "compact units are culture independent");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        return count;
    }
}
