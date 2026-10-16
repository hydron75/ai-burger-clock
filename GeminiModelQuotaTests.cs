using System.Text.Json.Nodes;

namespace AiBurgerClock;

// Synthetic group names, numbers and dates only. Never starts agy or opens the user DB.
internal static class GeminiModelQuotaTests
{
    private static readonly DateTimeOffset Now = new(2027, 2, 10, 10, 0, 0, TimeSpan.Zero);
    private const string Common = """
        {"name":"Gemini Models","buckets":[
          {"id":"gemini-weekly","window":"weekly","remaining_fraction":0.8,"reset_time":"2027-02-16T10:00:00Z"},
          {"id":"gemini-5h","window":"5h","remaining_fraction":0.7,"reset_time":"2027-02-10T13:00:00Z"}]}
        """;

    internal static async Task<int> RunAsync(string directory)
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Gemini model quota: " + label);
        }
        void Reject(JsonObject model, string label) => RejectJson(Data(model), label);
        void RejectJson(string json, string label)
        {
            try { AccountQuotaParsers.ParseGemini(json); Check(false, label); }
            catch (InvalidDataException error)
            {
                Check(error.Message == "CLI에서 유효한 구독 한도 정보를 받지 못했습니다.", label + "; no CLI payload");
            }
        }

        var reading = Read();
        Check(reading.Windows.Count == 3, "common and model-specific windows are all retained");
        Check(reading.Windows.Select(w => w.Label).SequenceEqual(["5시간", "주간", "주간 Gemini Synthetic Model"]),
            "five-hour, unchanged common weekly, then dynamic model weekly labels");
        Check(reading.Windows[0].Id == "gemini-5h" && reading.Windows[1].Id == "gemini-weekly",
            "existing common IDs and cached recovery identities are unchanged");
        Check(reading.Windows[2].WindowMinutes == 10080 && reading.Windows[2].UsedPercent == 60 &&
            reading.Windows[2].ResetsAtUtc == Now.AddDays(4), "scoped weekly uses only its own typed values");
        Check(reading.Windows[2].Id.Contains("Gemini Synthetic Model", StringComparison.Ordinal) &&
            reading.Windows[2].Id.EndsWith("gemini-synthetic-weekly", StringComparison.Ordinal), "identity contains original group and bucket");
        Check(AccountQuotaParsers.ParseGemini(Data()).Windows.Count == 2, "legacy response remains valid without a model group");
        Check(AccountQuotaParsers.ParseGemini(Data(Model("Gemini Synthetic Other"))).Windows[2].Label == "주간 Gemini Synthetic Other",
            "model name is not an Argon or other product-name allowlist");
        var reverse = new JsonObject { ["groups"] = new JsonArray(Model(), JsonNode.Parse(Common)) };
        Check(AccountQuotaParsers.ParseGemini(reverse.ToJsonString()).Windows.SequenceEqual(reading.Windows),
            "backend group order cannot reorder common and model rows");
        var multiple = new[] { Model("Gemini Synthetic Beta"), Model("Gemini Synthetic Alpha") };
        Check(AccountQuotaParsers.ParseGemini(Data(multiple)).Windows.Count == 4, "multiple distinct single-weekly model groups are supported");
        Check(AccountQuotaParsers.ParseGemini(Data(multiple.Select(m => (JsonObject)m.DeepClone()).Reverse().ToArray()))
            .Windows.Select(w => w.Id).SequenceEqual(AccountQuotaParsers.ParseGemini(Data(multiple)).Windows.Select(w => w.Id)),
            "model row identity and order remain stable across backend reordering");
        Check(Read(Model(id: "gemini-other-weekly")).Windows[2].Id != reading.Windows[2].Id,
            "changed raw bucket ID is a new identity");
        Check(Read(Model("Gemini Synthetic Other")).Windows[2].Id != reading.Windows[2].Id,
            "same raw ID reused by a different model cannot share a recovery identity");
        Check(Read(Model("Gemini Synthetic A", "gemini-x:gemini-y-weekly")).Windows[2].Id !=
            Read(Model("Gemini Synthetic A:gemini-x", "gemini-y-weekly")).Windows[2].Id,
            "length-prefixed pairs cannot collide when names or IDs contain separators");
        string longPrefix = "Gemini " + new string('x', 100);
        var longA = Read(Model(longPrefix + "A")).Windows[2];
        var longB = Read(Model(longPrefix + "B")).Windows[2];
        Check(longA.Label == longB.Label && longA.Id != longB.Id && longA.Label.Length <= 80,
            "shortened presentation never merges distinct original model scopes");
        Check(Read(Model(fraction: 0)).Windows[2].UsedPercent == 100, "raw zero fraction is exact model exhaustion");
        Check(Read(Model(fraction: 1)).Windows[2].UsedPercent == 0, "raw one fraction is unused model quota");
        Check(Read(Model(fraction: 1e-50)).Windows[2].RemainingPercent > 0, "tiny positive model balance is not exhaustion");
        var noReset = Model(); Bucket(noReset).Remove("reset_time");
        Check(Read(noReset).Windows[2].ResetsAtUtc is null, "absent model reset stays unknown");
        var nullReset = Model(); Bucket(nullReset)["reset_time"] = null;
        Check(Read(nullReset).Windows[2].ResetsAtUtc is null, "null model reset stays unknown");
        var offset = Model(); Bucket(offset)["reset_time"] = "2027-02-14T19:00:00+09:00";
        Check(Read(offset).Windows[2].ResetsAtUtc == Now.AddDays(4), "scoped reset offset normalizes to UTC");
        Check(AccountQuotaParsers.ParseGemini(Data(Model(), new JsonObject { ["name"] = "Claude and GPT models", ["buckets"] = null },
            new JsonObject { ["name"] = "Credits", ["balance"] = 123 })).Windows.SequenceEqual(reading.Windows),
            "third-party and credit groups are excluded, not combined into Gemini quotas");

        foreach (string name in new[] { "Gemini", "Gemini ", "Gemini  Synthetic", "GeminiSynthetic", "gemini Synthetic", " Gemini Synthetic", "Gemini Synthetic ", "Gemini Synthetic\nsecret" })
            Reject(Model(name), "ambiguous Gemini name namespace: " + name.Replace('\n', ' '));
        Reject(Model("Gemini " + new string('x', 250)), "overlong model name");
        foreach (string id in new[] { "gemini-weekly", "gemini-5h", "other-synthetic-weekly", "Gemini-synthetic-weekly", "gemini-synthetic-Weekly",
            "gemini--weekly", "gemini-synthetic-daily", "gemini-synthetic weekly", "gemini-secret\n-weekly", "gemini- secret-weekly" })
        {
            var model = Model(id: id);
            Reject(model, "ambiguous or reserved model bucket ID");
        }
        foreach (string field in new[] { "id", "window", "remaining_fraction" })
        {
            var missing = Model(); Bucket(missing).Remove(field); Reject(missing, "missing model " + field);
            var nullField = Model(); Bucket(nullField)[field] = null; Reject(nullField, "null model " + field);
        }
        foreach (string slot in new[] { "5h", "monthly", "Weekly" })
        {
            var model = Model(); Bucket(model)["window"] = slot; Reject(model, "model period must be exactly weekly");
        }
        foreach (JsonNode? invalid in new JsonNode?[] { null, new JsonObject(), new JsonArray(), JsonValue.Create("0.4"), JsonValue.Create(true), JsonValue.Create(-0.1), JsonValue.Create(1.1) })
        {
            var model = Model(); Bucket(model)["remaining_fraction"] = invalid?.DeepClone(); Reject(model, "model fraction must be a finite number in range");
        }
        RejectJson(Data(Model()).Replace("\"remaining_fraction\":0.4", "\"remaining_fraction\":1e999", StringComparison.Ordinal), "non-finite model fraction");
        foreach (JsonNode invalid in new JsonNode[] { JsonValue.Create(123)!, JsonValue.Create("")!, JsonValue.Create("2027-02-14T10:00:00")!, JsonValue.Create("invalid")! })
        {
            var model = Model(); Bucket(model)["reset_time"] = invalid; Reject(model, "malformed model reset");
        }
        foreach (JsonNode? buckets in new JsonNode?[] { null, new JsonObject(), new JsonArray(), new JsonArray(Model()["buckets"]![0]!.DeepClone(), Model()["buckets"]![0]!.DeepClone()) })
        {
            var model = Model(); model["buckets"] = buckets; Reject(model, "model group must have exactly one bucket");
        }
        var notObject = Model(); notObject["buckets"]![0] = "invalid"; Reject(notObject, "model bucket must be an object");
        RejectJson(Data(Model(), Model()), "duplicate Gemini model group rejects the whole response");
        RejectJson(new JsonObject { ["groups"] = new JsonArray(Model()) }.ToJsonString(), "model group cannot replace the required common group");
        var unrecognizedCommon = JsonNode.Parse(Common)!;
        unrecognizedCommon["buckets"]!.AsArray().Add(Bucket(Model()).DeepClone());
        RejectJson(new JsonObject { ["groups"] = new JsonArray(unrecognizedCommon) }.ToJsonString(), "new bucket inside common group remains an error");
        var tooLongId = Model("Gemini " + new string('x', 249), "gemini-" + new string('y', 242) + "-weekly");
        Reject(tooLongId, "combined identity is bounded to the cache and notification limit");
        Check(Read(Model("Gemini " + new string('x', 249), "gemini-" + new string('y', 225) + "-weekly")).Windows[2].Id.Length == 512,
            "an identity at the exact downstream size limit remains valid");
        Check(AccountQuotaParsers.ParseGemini(Data(Enumerable.Range(0, 62).Select(i => Model("Gemini Synthetic " + i)).ToArray())).Windows.Count == 64,
            "total common plus model windows may reach 64");
        RejectJson(Data(Enumerable.Range(0, 63).Select(i => Model("Gemini Synthetic " + i)).ToArray()), "total window bound spans all groups");

        PacingAndRecovery(Check, reading);
        var store = new UsageStore(Path.Combine(directory, "gemini-model-cache.db"));
        await store.InitializeAsync();
        await store.SaveQuotaAsync(new(1, AccountQuotaParsers.ParseGemini(Data()), Now, []));
        Check((await store.ReadQuotaAsync(QuotaProvider.Gemini))!.Reading.Windows.Count == 2, "existing two-window cache still loads");
        await store.SaveQuotaAsync(new(1, reading, Now, []));
        var reopened = new UsageStore(store.DatabasePath);
        var cached = await reopened.ReadQuotaAsync(QuotaProvider.Gemini);
        Check(cached!.Reading.Windows.SequenceEqual(reading.Windows), "three-window cache survives a new store instance without migration");
        Check(cached.Version == 1 && UsageStore.SchemaVersion == 2, "cache version and DB schema are unchanged");
        return count;
    }

    private static void PacingAndRecovery(Action<bool, string> check, QuotaReading reading)
    {
        var state = new QuotaState(QuotaProvider.Gemini, reading, Now, Now, Now.AddHours(6));
        var section = QuotaPanelModel.Section(state, Now);
        check(section.Rows.Count == 3 && section.Rows[2].Text.StartsWith("주간 Gemini Synthetic Model  60% 사용", StringComparison.Ordinal), "panel displays the scoped row after common weekly");
        check(section.Rows[2].ElapsedPercent is > 42 and < 43 && section.Rows[2].ComparisonText!.Contains("균등 사용 기준보다", StringComparison.Ordinal), "model weekly has a time layer, boundary and shared accessibility comparison");
        check(QuotaPanelModel.Section(state, Now.AddMinutes(17)).Rows[2].ElapsedPercent == section.Rows[2].ElapsedPercent &&
            QuotaPanelModel.Section(state, Now.AddMinutes(17)).Rows[2].ComparisonText!.EndsWith("17분 전 기준", StringComparison.Ordinal), "model comparison is pinned while its reference age advances");
        check(QuotaPanelModel.Section(state with { IsPrevious = true }, Now).Rows[2].ElapsedPercent is null, "previous model reading hides time comparison");
        check(QuotaPacing.Calculate(reading.Windows[2] with { ResetsAtUtc = null }, Now, false, Now).ElapsedPercent is null, "missing model reset hides comparison");
        check(QuotaPacing.Calculate(reading.Windows[2], Now, false, Now.AddDays(4)).ElapsedPercent is null, "elapsed model reset hides comparison");
        check(AccountQuotaPolicy.GetInterval(Now, Read(Model(fraction: 0)).Windows) == TimeSpan.FromMinutes(15), "model-only exhaustion selects existing fifteen-minute policy");
        check(AccountQuotaPolicy.GetInterval(Now, Read(Model(fraction: 0.05)).Windows) == TimeSpan.FromHours(1), "model-only low balance selects existing hourly policy");
        var nearReset = Model(); Bucket(nearReset)["reset_time"] = Now.AddMinutes(10).ToString("O");
        check(AccountQuotaPolicy.GetInterval(Now, Read(nearReset).Windows) == TimeSpan.FromMinutes(5), "model-only near reset participates in existing five-minute policy");

        var engine = new QuotaRecoveryNotifications();
        long sequence = 0;
        QuotaRecoveryAlert? Observe(QuotaReading value) => engine.Observe(new(QuotaProvider.Gemini, ++sequence, Now.AddSeconds(sequence), value), true);
        check(Observe(AccountQuotaParsers.ParseGemini(Data())) is null, "common-only live response seeds quietly");
        check(Observe(reading) is null, "first appearance of an available model window is not recovery");
        check(Observe(Read(Model(fraction: 0))) is null, "model exhaustion arms quietly");
        var alert = Observe(reading);
        check(alert is not null && alert.WindowIds.SequenceEqual([reading.Windows[2].Id]) && alert.Body.Contains("주간 Gemini Synthetic Model", StringComparison.Ordinal), "only the same exhausted model scope recovers");
        check(!alert!.Body.Contains('%') && !alert.Body.Contains(reading.Windows[2].Id, StringComparison.Ordinal), "model alert excludes account values and internal identity");
        check(Observe(reading) is null, "unchanged model recovery does not repeat");
        check(Observe(Read(Model(fraction: 0))) is null, "model can become exhausted again");
        check(Observe(Read(Model("Gemini Synthetic Renamed"))) is null, "same raw bucket under a new model scope seeds quietly");
        check(Observe(Read(Model(fraction: 0))) is null, "reappearing model seeds quietly");
        check(Observe(AccountQuotaParsers.ParseGemini(Data())) is null && Observe(reading) is null, "missing then available model window is not recovery");
        var off = new QuotaRecoveryNotifications();
        check(off.Observe(new(QuotaProvider.Gemini, 1, Now, Read(Model(fraction: 0))), false) is null &&
            off.Observe(new(QuotaProvider.Gemini, 2, Now.AddSeconds(1), reading), false) is null, "model row never enables the default-off notification setting");
    }

    private static JsonObject Model(string name = "Gemini Synthetic Model", string id = "gemini-synthetic-weekly", double fraction = 0.4) => new()
    {
        ["name"] = name,
        ["description"] = "Synthetic model scope; never used for classification",
        ["buckets"] = new JsonArray(new JsonObject { ["id"] = id, ["window"] = "weekly", ["remaining_fraction"] = fraction, ["reset_time"] = "2027-02-14T10:00:00Z" })
    };
    private static JsonObject Bucket(JsonObject model) => model["buckets"]![0]!.AsObject();
    private static string Data(params JsonObject[] models)
    {
        var groups = new JsonArray(JsonNode.Parse(Common));
        foreach (var model in models) groups.Add(model.DeepClone());
        return new JsonObject { ["groups"] = groups }.ToJsonString();
    }
    private static QuotaReading Read(JsonObject? model = null) => AccountQuotaParsers.ParseGemini(Data(model ?? Model()));
}
