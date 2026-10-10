using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiBurgerClock;

/// <summary>Deterministic feed fixtures and in-memory HTTP tests; never contacts the Internet.</summary>
internal static class ProviderStatusTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-19T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string Catalog = """{"products":[{"id":"gemini","title":"Gemini"},{"id":"notebook","title":"Gemini Notebook"},{"id":"gmail","title":"Gmail"}]}""";

    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string description)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Provider test failed: " + description);
        }
        ProviderStatus Parse(string json, ProviderKind kind = ProviderKind.OpenAI) => ProviderStatusClient.ParseStatuspage(kind, json, Now);
        void Expect(string label, string json, OfficialStatus expected, ProviderKind kind = ProviderKind.OpenAI) =>
            Check(Parse(json, kind).Status == expected, label);

        Expect("OpenAI healthy", Page([Component("ChatGPT", "operational")]), OfficialStatus.Operational);
        Expect("Claude healthy", Page([Component("claude.ai", "operational"), Component("Claude API (api.anthropic.com)", "operational"), Component("Claude Code", "operational")]), OfficialStatus.Operational, ProviderKind.Claude);
        Expect("OpenAI image-only outage does not lower Agent health", Page([Component("ChatGPT", "operational"), Component("Images", "major_outage")], indicator: "critical"), OfficialStatus.Operational);
        Expect("OpenAI voice Sora outages ignored", Page([Component("Codex Web", "operational"), Component("Voice mode", "major_outage"), Component("Sora", "major_outage")]), OfficialStatus.Operational);
        Expect("Claude console-only outage ignored", Page([Component("Claude Code", "operational"), Component("Claude Console (platform.claude.com)", "major_outage")], indicator: "major"), OfficialStatus.Operational, ProviderKind.Claude);
        Expect("Claude government-only outage ignored", Page([Component("claude.ai", "operational"), Component("Claude for Government", "major_outage")]), OfficialStatus.Operational, ProviderKind.Claude);
        Expect("OpenAI degraded", Page([Component("Agent", "degraded_performance")]), OfficialStatus.Degraded);
        Expect("OpenAI partial", Page([Component("Codex Web", "partial_outage")]), OfficialStatus.PartialOutage);
        Expect("Claude major", Page([Component("Claude Code", "major_outage")]), OfficialStatus.MajorOutage, ProviderKind.Claude);
        Expect("Maintenance is caution", Page([Component("Files", "under_maintenance")]), OfficialStatus.Degraded);
        Expect("Known OpenAI id tolerates rename", Page([Component("Coding workspace renamed", "partial_outage", "01JVCV8YSWZFRSM1G5CVP253SK")]), OfficialStatus.PartialOutage);
        Expect("Known Claude id tolerates rename", Page([Component("Code workspace renamed", "degraded_performance", "yyzkbfz2thpt")]), OfficialStatus.Degraded, ProviderKind.Claude);
        Expect("New Codex id semantic match", Page([Component("Codex CLI", "degraded_performance", "new-id")]), OfficialStatus.Degraded);
        Expect("New ChatGPT Work component", Page([Component("ChatGPT Work", "operational", "new-id")]), OfficialStatus.Operational);
        Expect("Missing component status unknown", Page([new { id = "work", name = "ChatGPT Work" }]), OfficialStatus.Unknown);
        Expect("Unrecognized status unknown", Page([Component("Agent", "new-severity")]), OfficialStatus.Unknown);
        Expect("Missing relevant component unknown", Page([Component("Sora", "operational")]), OfficialStatus.Unknown);
        Expect("Malformed JSON unknown", "{broken", OfficialStatus.Unknown);
        Expect("Wrong root unknown", "[]", OfficialStatus.Unknown);
        Expect("Empty object unknown", "{}", OfficialStatus.Unknown);
        Expect("Changed incidents schema unknown", """{"components":[{"id":"a","name":"Agent","status":"operational"}],"incidents":{},"status":{"indicator":"none"}}""", OfficialStatus.Unknown);
        Expect("Unknown overall schema unknown", Page([Component("Agent", "operational")], indicator: "new-type"), OfficialStatus.Unknown);
        Expect("Relevant incident overrides normal component", Page([Component("ChatGPT", "operational", "chat")], [Incident("ChatGPT Work interruption", "major", [new { id = "chat" }])]), OfficialStatus.PartialOutage);
        Expect("Relevant unlinked incident title", Page([Component("Codex Web", "operational")], [Incident("Elevated errors in Codex", "minor")]), OfficialStatus.Degraded);
        Expect("Resolved incident ignored", Page([Component("ChatGPT", "operational")], [Incident("ChatGPT outage", "critical", status: "resolved")]), OfficialStatus.Operational);
        Expect("Unrelated linked incident ignored", Page([Component("ChatGPT", "operational"), Component("Images", "major_outage", "image")], [Incident("Service failure", "critical", [new { id = "image" }])]), OfficialStatus.Operational);
        Expect("Image-only incident with ChatGPT broad component ignored", Page([Component("ChatGPT", "operational", "chat")], [Incident("ChatGPT image generation failure", "major", [new { id = "chat" }])]), OfficialStatus.Operational);
        Expect("Agent API billing impact none ignored", Page([Component("Agent", "operational")], [Incident("Overbilling for OpenAI-hosted containers in the Agent API", "none")]), OfficialStatus.Operational);
        Expect("Unknown incident scope not assumed healthy", Page([Component("ChatGPT", "operational")], [Incident("Elevated error rates", "major")]), OfficialStatus.Unknown);
        Expect("Unknown impact-none incident not assumed unrelated", Page([Component("ChatGPT", "operational")], [Incident("Investigating elevated errors", "none")]), OfficialStatus.Unknown);
        Expect("Unknown incident lifecycle unknown", Page([Component("ChatGPT", "operational")], [Incident("ChatGPT issue", "major", status: "new-state")]), OfficialStatus.Unknown);
        Expect("Unknown incident impact unknown", Page([Component("ChatGPT", "operational")], [Incident("ChatGPT issue", "new-impact")]), OfficialStatus.Unknown);
        Expect("Component string IDs supported", Page([Component("ChatGPT", "operational", "chat")], [Incident("Service degradation", "minor", ["chat"])]), OfficialStatus.Degraded);
        Expect("Claude Cowork in scope", Page([Component("Claude Cowork", "partial_outage")]), OfficialStatus.PartialOutage, ProviderKind.Claude);
        var metadata = Parse(Page([Component("ChatGPT", "operational", "chat")], [Incident("ChatGPT interruption", "major", ["chat"])]));
        Check(metadata.IncidentId == "test-incident" && metadata.IncidentTitle == "ChatGPT interruption" && metadata.RelevantComponent.Contains("ChatGPT"), "incident and component retained");
        Check(metadata.CheckedAtUtc == Now && metadata.LastSuccessfulCheckUtc == Now, "successful check timestamp");
        Check(Parse("{}").LastSuccessfulCheckUtc is null, "unknown parsing not marked successful");

        // Regression for the 2026-09-22 official response shape: active incident, minor impact,
        // no component links, and healthy summary components. IDs are deterministic test values.
        const string planUsersFixture = """
            {
              "page": { "id": "openai-fixture", "name": "OpenAI" },
              "status": { "indicator": "none", "description": "All Systems Operational" },
              "components": [{ "id": "chat", "name": "ChatGPT", "status": "operational" }],
              "incidents": [{
                "id": "plus-pro-20260922",
                "name": "Increased error rate for Plus and Pro users.",
                "status": "investigating",
                "impact": "minor"
              }]
            }
            """;
        var planIssue = Parse(planUsersFixture);
        Check(planIssue.Status == OfficialStatus.Degraded, "confirmed Plus and Pro users incident degrades OpenAI");
        Check(planIssue.RelevantComponent == "ChatGPT Plus/Pro", "plan wording scope explicitly attributed to ChatGPT Plus/Pro");
        Check(planIssue.IncidentId == "plus-pro-20260922" && planIssue.IncidentTitle == "Increased error rate for Plus and Pro users.", "plan incident metadata retained");
        Check(planIssue.LastSuccessfulCheckUtc == Now, "recognized plan incident is a successful status check");
        Expect("Plan wording case insensitive with word boundaries", Page([Component("ChatGPT", "operational")], [Incident("Errors for (PLUS AND PRO USERS).", "minor")]), OfficialStatus.Degraded);
        Expect("Plan wording tolerates whitespace", Page([Component("ChatGPT", "operational")], [Incident("Errors for Plus  and\tPro users.", "minor")]), OfficialStatus.Degraded);
        Expect("Resolved plan incident ignored", Page([Component("ChatGPT", "operational")], [Incident("Increased error rate for Plus and Pro users.", "minor", status: "resolved")]), OfficialStatus.Operational);
        foreach (string title in new[] { "Pro users are experiencing errors", "Plus users are experiencing errors", "Plus and Pro errors", "UltraPlus and Pro users have errors", "Plus and Pro userspace errors" })
            Expect("Ambiguous plan wording remains unknown: " + title, Page([Component("ChatGPT", "operational")], [Incident(title, "minor")]), OfficialStatus.Unknown);
        foreach (string excluded in new[] { "Sora", "voice", "images", "billing" })
            Expect("Excluded product beats plan wording: " + excluded,
                Page([Component("ChatGPT", "operational")], [Incident($"{excluded} issues for Plus and Pro users", "minor")]), OfficialStatus.Operational);
        Expect("Explicit unrelated component beats plan title inference", Page([Component("ChatGPT", "operational"), Component("Images", "operational", "images")],
            [Incident("Increased error rate for Plus and Pro users.", "minor", ["images"])]), OfficialStatus.Operational);
        Expect("Plan title rule is OpenAI-only", Page([Component("claude.ai", "operational")],
            [Incident("Increased error rate for Plus and Pro users.", "minor")]), OfficialStatus.Unknown, ProviderKind.Claude);
        var unscoped = Parse(Page([Component("ChatGPT", "operational")], [Incident("Elevated error rates", "major")]));
        Check(unscoped.IncidentId.Length == 0 && unscoped.IncidentTitle.Length == 0 && unscoped.RelevantComponent == "" &&
            unscoped.UncertainIncidentId == "test-incident" && unscoped.UncertainIncidentTitle == "Elevated error rates",
            "unscoped incident is separate from confirmed incident metadata without an invented component");
        Check(unscoped.LastSuccessfulCheckUtc is null, "pure parsing does not certify transport success for an unknown scope");
        foreach (var unknownIncident in new[]
        {
            Incident("ChatGPT interruption", "minor", ["chat"], "new-lifecycle"),
            Incident("ChatGPT interruption", "new-impact", ["chat"])
        })
        {
            var unknownIssue = Parse(Page([Component("ChatGPT", "operational", "chat")], [unknownIncident]));
            Check(unknownIssue.Status == OfficialStatus.Unknown && unknownIssue.RelevantComponent == "ChatGPT" &&
                unknownIssue.IncidentId.Length == 0 && unknownIssue.IncidentTitle.Length == 0 &&
                unknownIssue.UncertainIncidentId == "test-incident" && unknownIssue.UncertainIncidentTitle == "ChatGPT interruption",
                "unknown relevant incident lifecycle or impact uses separate uncertain metadata");
        }
        var administrative = Parse(Page([Component("ChatGPT", "operational")],
            [Incident("Delayed administrative metric processing", "minor", status: "monitoring")]));
        Check(administrative.Status == OfficialStatus.Unknown && administrative.AssessmentIssue.Length > 0 &&
            administrative.UncertainIncidentTitle == "Delayed administrative metric processing" && administrative.IncidentTitle.Length == 0,
            "healthy components plus an unlinked generic monitoring incident stay CHECK, not a confirmed outage");
        var mixed = Parse(Page([Component("ChatGPT", "operational", "chat")],
            [Incident("ChatGPT interruption", "major", ["chat"]),
                new { id = "uncertain-test", name = "Delayed administrative metric processing", status = "monitoring", impact = "minor" }]));
        Check(mixed.Status == OfficialStatus.PartialOutage && mixed.IncidentId == "test-incident" && mixed.IncidentTitle == "ChatGPT interruption",
            "a confirmed relevant outage keeps its severity and confirmed incident metadata");
        Check(mixed.UncertainIncidentId == "uncertain-test" && mixed.UncertainIncidentTitle == "Delayed administrative metric processing" &&
            mixed.AssessmentIssue.Length > 0, "a simultaneous unknown-scope incident is retained separately from a confirmed outage");
        var recovered = Parse(Page([Component("ChatGPT", "operational")]));
        Check(recovered.IncidentId.Length == 0 && recovered.IncidentTitle.Length == 0 &&
            recovered.UncertainIncidentId.Length == 0 && recovered.UncertainIncidentTitle.Length == 0,
            "prior parsed confirmed or uncertain incident metadata never leaks into a later response");

        ProviderStatus Google(string history, string catalog = Catalog) => ProviderStatusClient.ParseGoogle(catalog, history, Now);
        void GoogleExpect(string label, string history, OfficialStatus expected) => Check(Google(history).Status == expected, label);
        GoogleExpect("Gemini no incidents normal", "[]", OfficialStatus.Operational);
        GoogleExpect("Gemini active disruption", GoogleHistory("gemini", "SERVICE_DISRUPTION"), OfficialStatus.PartialOutage);
        GoogleExpect("Gemini active outage", GoogleHistory("gemini", "SERVICE_OUTAGE"), OfficialStatus.MajorOutage);
        GoogleExpect("Gemini informational incident caution", GoogleHistory("gemini", "SERVICE_INFORMATION"), OfficialStatus.Degraded);
        GoogleExpect("Gemini resolved outage ignored", GoogleHistory("gemini", "SERVICE_OUTAGE", "2026-09-18T08:00:00Z"), OfficialStatus.Operational);
        GoogleExpect("Gemini recent AVAILABLE wins old impact", GoogleHistory("gemini", "AVAILABLE"), OfficialStatus.Operational);
        GoogleExpect("Gmail outage ignored", GoogleHistory("gmail", "SERVICE_OUTAGE"), OfficialStatus.Operational);
        GoogleExpect("Gemini Notebook separate product", GoogleHistory("notebook", "SERVICE_OUTAGE"), OfficialStatus.Operational);
        GoogleExpect("Unknown Gemini state", GoogleHistory("gemini", "NEW_SEVERITY"), OfficialStatus.Unknown);
        GoogleExpect("Invalid Gemini date unknown", GoogleHistory("gemini", "AVAILABLE", begin: "not-a-date"), OfficialStatus.Unknown);
        GoogleExpect("Future incident ignored", GoogleHistory("gemini", "SERVICE_OUTAGE", begin: "2026-09-20T00:00:00Z"), OfficialStatus.Operational);
        GoogleExpect("Malformed Google feed", "oops", OfficialStatus.Unknown);
        GoogleExpect("Wrong Google history root", "{}", OfficialStatus.Unknown);
        GoogleExpect("Unscoped Google incident unknown", "[{\"begin\":\"2026-09-18T00:00:00Z\"}]", OfficialStatus.Unknown);
        Check(Google("[]", "{}").Status == OfficialStatus.Unknown, "invalid catalog");
        Check(Google("[]", """{"products":[{"id":"mail","title":"Gmail"}]}""").Status == OfficialStatus.Unknown, "no Gemini catalog entry");
        Check(Google("[]", """{"products":[{"id":"npdyhgECDJ6tB66MxXyo","title":"Renamed Gemini product"}]}""").Status == OfficialStatus.Operational, "stable Gemini ID after rename");
        Check(Google(GoogleHistory("gemini", "SERVICE_OUTAGE")).IncidentId == "google-incident", "Google incident id retained");
        GoogleExpect("Legacy Google service_key supported", """[{"id":"legacy","service_key":"gemini","begin":"2026-09-18T00:00:00Z","status_impact":"SERVICE_DISRUPTION"}]""", OfficialStatus.PartialOutage);

        count += Task.Run(TransportTestsAsync).GetAwaiter().GetResult();
        return count;
    }

    private static object Component(string name, string status, string? id = null) => new { id = id ?? name, name, status };
    private static object Incident(string title, string impact, object[]? components = null, string status = "investigating") =>
        new { id = "test-incident", name = title, status, impact, components = components ?? [] };
    private static string Page(object[] components, object[]? incidents = null, string indicator = "none") =>
        JsonSerializer.Serialize(new { components, incidents = incidents ?? [], status = new { indicator } });
    private static string GoogleHistory(string product, string status, string? end = null, string begin = "2026-09-18T00:00:00Z") =>
        JsonSerializer.Serialize(new[] { new { id = "google-incident", begin, end, external_desc = "Gemini incident", affected_products = new[] { new { id = product, title = product } }, status_impact = "SERVICE_OUTAGE", most_recent_update = new { status } } });

    private static async Task<int> TransportTestsAsync()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Provider transport test failed: " + label);
        }
        async Task AssertThrows<T>(Func<Task> action, string label) where T : Exception
        {
            count++;
            try { await action().ConfigureAwait(false); }
            catch (T) { return; }
            throw new InvalidOperationException("Provider transport test failed: " + label);
        }
        string healthyComponents = JsonSerializer.Serialize(new { components = new[] { Component("Agent", "operational") } });
        string missingIncidents = JsonSerializer.Serialize(new { components = new[] { Component("Agent", "operational") }, status = new { indicator = "none" } });
        foreach (var fixture in new[]
        {
            ("{\"incidents\":[]}", OfficialStatus.Operational),
            (JsonSerializer.Serialize(new { incidents = new[] { Incident("ChatGPT outage", "critical", status: "resolved") } }), OfficialStatus.Operational),
            (JsonSerializer.Serialize(new { incidents = new[] { Incident("ChatGPT Work outage", "major") } }), OfficialStatus.PartialOutage),
            ("{}", OfficialStatus.Unknown),
            ("{\"incidents\":null}", OfficialStatus.Unknown),
            ("broken", OfficialStatus.Unknown)
        })
        {
            var requests = new List<string>();
            using var fallbackClient = new HttpClient(new FakeHandler((request, _) =>
            {
                string address = request.RequestUri!.AbsoluteUri;
                requests.Add(address);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(address switch
                    {
                        ProviderStatusClient.OpenAiSummaryUrl => missingIncidents,
                        ProviderStatusClient.OpenAiComponentsUrl => healthyComponents,
                        ProviderStatusClient.OpenAiIncidentsUrl => fixture.Item1,
                        _ => throw new InvalidOperationException("Unexpected official feed address")
                    })
                });
            }));
            var result = await new ProviderStatusClient(fallbackClient, () => Now).FetchAsync(ProviderKind.OpenAI, CancellationToken.None);
            count++;
            if (result.Status != fixture.Item2 || requests.Count != 3 ||
                !requests.Order(StringComparer.Ordinal).SequenceEqual(new[]
                {
                    ProviderStatusClient.OpenAiSummaryUrl, ProviderStatusClient.OpenAiComponentsUrl, ProviderStatusClient.OpenAiIncidentsUrl
                }.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("Missing incidents must use a validated official history feed");
            Check(result.LastSuccessfulCheckUtc == Now, "complete fallback response receipt counts as success even when its schema is unknown");
        }
        using (var fallbackClient = new HttpClient(new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsoluteUri switch
            {
                ProviderStatusClient.OpenAiSummaryUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(missingIncidents) },
                ProviderStatusClient.OpenAiComponentsUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(healthyComponents) },
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            }))))
            await AssertThrows<HttpRequestException>(() => new ProviderStatusClient(fallbackClient).FetchAsync(ProviderKind.OpenAI, CancellationToken.None), "history fallback HTTP failure cannot become healthy");
        using (var catalogClient = new HttpClient(new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsoluteUri == ProviderStatusClient.OpenAiSummaryUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page([Component("ChatGPT", "operational")])) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
            await AssertThrows<HttpRequestException>(() => new ProviderStatusClient(catalogClient).FetchAsync(ProviderKind.OpenAI, CancellationToken.None),
                "a required full component catalog HTTP failure is a receipt failure, not false GO");

        async Task<ProviderStatus> FetchOpenAiAsync(string summary, string catalog, List<string>? requests = null)
        {
            using var fixtureClient = new HttpClient(new FakeHandler((request, _) =>
            {
                string address = request.RequestUri!.AbsoluteUri;
                requests?.Add(address);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(address switch
                    {
                        ProviderStatusClient.OpenAiSummaryUrl => summary,
                        ProviderStatusClient.OpenAiComponentsUrl => catalog,
                        _ => throw new InvalidOperationException("A summary with incidents must not request incident history")
                    }, Encoding.UTF8, "application/json")
                });
            }));
            return await new ProviderStatusClient(fixtureClient, () => Now).FetchAsync(ProviderKind.OpenAI, CancellationToken.None);
        }

        foreach (string name in new[] { "Codex Web", "Codex API", "Codex CLI", "Codex VS Code extension" })
        {
            var supplemented = await FetchOpenAiAsync(Page([Component("ChatGPT", "operational", "chat")]),
                JsonSerializer.Serialize(new { components = new[] { Component(name, "partial_outage", "catalog-codex") } }));
            Check(supplemented.Status == OfficialStatus.PartialOutage && supplemented.RelevantComponent.Contains(name, StringComparison.Ordinal),
                "missing summary component outage is supplied by the full catalog: " + name);
            Check(supplemented.LastSuccessfulCheckUtc == Now, "full catalog success timestamp: " + name);
        }
        var priority = await FetchOpenAiAsync(Page([Component("Codex CLI", "partial_outage", "codex-cli")]),
            JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", "operational", "codex-cli") } }));
        Check(priority.Status == OfficialStatus.PartialOutage,
            "catalog operational cannot erase a known summary outage for the same component ID");
        var catalogOutage = await FetchOpenAiAsync(Page([Component("Codex CLI", "operational", "codex-cli")]),
            JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", "partial_outage", "codex-cli") } }));
        Check(catalogOutage.Status == OfficialStatus.PartialOutage,
            "summary operational cannot erase a known catalog outage for the same component ID");
        foreach (bool unknownInSummary in new[] { true, false })
        {
            string summaryStatus = unknownInSummary ? "synthetic-new-status" : "operational";
            string catalogStatus = unknownInSummary ? "operational" : "synthetic-new-status";
            var disagreement = await FetchOpenAiAsync(Page([Component("Codex CLI", summaryStatus, "codex-cli")]),
                JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", catalogStatus, "codex-cli") } }));
            Check(disagreement.Status == OfficialStatus.Unknown && disagreement.AssessmentIssue.Length > 0 &&
                disagreement.LastSuccessfulCheckUtc == Now,
                "unknown component observation cannot be overwritten by operational: summary=" + unknownInSummary);
        }
        foreach (bool unknownInSummary in new[] { true, false })
        {
            string summaryStatus = unknownInSummary ? "synthetic-new-status" : "partial_outage";
            string catalogStatus = unknownInSummary ? "partial_outage" : "synthetic-new-status";
            var disagreement = await FetchOpenAiAsync(Page([Component("Codex CLI", summaryStatus, "codex-cli")]),
                JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", catalogStatus, "codex-cli") } }));
            Check(disagreement.Status == OfficialStatus.PartialOutage && disagreement.AssessmentIssue.Length > 0 &&
                disagreement.LastSuccessfulCheckUtc == Now,
                "known outage wins while the conflicting unknown observation remains diagnostic: summary=" + unknownInSummary);
        }
        var duplicateHealthy = await FetchOpenAiAsync(Page([Component("Codex CLI", "operational", "codex-cli")]),
            JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", "operational", "codex-cli") } }));
        Check(duplicateHealthy.Status == OfficialStatus.Operational && duplicateHealthy.AssessmentIssue.Length == 0 &&
            duplicateHealthy.LastSuccessfulCheckUtc == Now, "identical component observations remain a healthy successful receipt");
        var conflictingOutages = await FetchOpenAiAsync(Page([Component("Codex CLI", "partial_outage", "codex-cli")]),
            JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", "major_outage", "codex-cli") } }));
        Check(conflictingOutages.Status == OfficialStatus.MajorOutage,
            "different known component severities retain the more severe observed outage");
        var union = await FetchOpenAiAsync(Page([Component("ChatGPT", "partial_outage", "summary-only")]),
            JsonSerializer.Serialize(new { components = new[] { Component("Codex CLI", "operational", "catalog-only") } }));
        Check(union.Status == OfficialStatus.PartialOutage && union.RelevantComponent.Contains("ChatGPT", StringComparison.Ordinal),
            "summary-only relevant components remain in the catalog union");
        var directRequests = new List<string>();
        var direct = await FetchOpenAiAsync(Page([Component("ChatGPT", "operational")]), healthyComponents, directRequests);
        Check(directRequests.Count == 2 && directRequests.Contains(ProviderStatusClient.OpenAiSummaryUrl) &&
            directRequests.Contains(ProviderStatusClient.OpenAiComponentsUrl), "normal OpenAI fetch always reads summary and components without incident history");
        Check(direct.Source.Contains(ProviderStatusClient.OpenAiSummaryUrl, StringComparison.Ordinal) &&
            direct.Source.Contains(ProviderStatusClient.OpenAiComponentsUrl, StringComparison.Ordinal), "OpenAI source records both public component feeds");
        foreach (string malformedCatalog in new[]
        {
            "{broken", "{}", "{\"components\":null}", "{\"components\":[{\"id\":\"invalid-component\"}]}"
        })
        {
            var unknown = await FetchOpenAiAsync(Page([Component("ChatGPT", "operational")]), malformedCatalog);
            Check(unknown.Status == OfficialStatus.Unknown && unknown.LastSuccessfulCheckUtc == Now && unknown.AssessmentIssue.Length > 0,
                "malformed full catalog receipt succeeds but cannot falsely certify GO");
        }
        var malformedSummary = await FetchOpenAiAsync("{broken", healthyComponents);
        Check(malformedSummary.Status == OfficialStatus.Unknown && malformedSummary.LastSuccessfulCheckUtc == Now && malformedSummary.AssessmentIssue.Length > 0,
            "malformed summary receipt succeeds but its assessment remains CHECK");
        using (var client = new HttpClient(new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsoluteUri switch
            {
                ProviderStatusClient.GoogleProductsUrl => Catalog,
                ProviderStatusClient.GoogleIncidentsUrl => "[]",
                ProviderStatusClient.ClaudeSummaryUrl => Page([Component("Claude Code", "operational")]),
                _ => Page([Component("Agent", "operational")])
            }, Encoding.UTF8, "application/json")
        }))))
        {
            var source = new ProviderStatusClient(client);
            foreach (var kind in Enum.GetValues<ProviderKind>())
            {
                count++;
                if ((await source.FetchAsync(kind, CancellationToken.None).ConfigureAwait(false)).Status != OfficialStatus.Operational)
                    throw new InvalidOperationException("Provider fake transport routing failed");
            }
        }
        string? userAgent = null;
        using (var client = new HttpClient(new FakeHandler((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Page([Component("Claude Code", "operational")]), Encoding.UTF8, "application/json")
            });
        })))
            await new ProviderStatusClient(client).FetchAsync(ProviderKind.Claude, CancellationToken.None).ConfigureAwait(false);
        count++;
        if (userAgent != ProviderStatusClient.UserAgent || !userAgent.StartsWith("AIBurgerClock/", StringComparison.Ordinal))
            throw new InvalidOperationException("Status requests carry one versioned User-Agent: " + userAgent);
        using (var client = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("offline"))))
            await AssertThrows<HttpRequestException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.OpenAI, CancellationToken.None), "offline propagation");
        using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
            await AssertThrows<HttpRequestException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.Claude, CancellationToken.None), "HTTP failure");
        using (var client = new HttpClient(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { Timeout = TimeSpan.FromMilliseconds(80) })
            await AssertThrows<OperationCanceledException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.OpenAI, CancellationToken.None), "timeout");
        using (var cancellation = new CancellationTokenSource())
        using (var client = new HttpClient(new FakeHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })))
        {
            cancellation.Cancel();
            await AssertThrows<OperationCanceledException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.OpenAI, cancellation.Token), "shutdown cancellation");
        }
        using (var client = new HttpClient(new FakeHandler((_, _) =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = ProviderStatusClient.MaxResponseBytes + 1L;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        })))
            await AssertThrows<InvalidDataException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.OpenAI, CancellationToken.None), "body size cap");
        using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnseekableMemoryStream(new byte[ProviderStatusClient.MaxResponseBytes + 1]))
        }))))
            await AssertThrows<InvalidDataException>(() => new ProviderStatusClient(client).FetchAsync(ProviderKind.OpenAI, CancellationToken.None), "streamed body cap without Content-Length");
        return count;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class UnseekableMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
