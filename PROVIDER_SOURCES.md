# Official provider sources (verified 2026-09-19; OpenAI follow-ups 2026-09-22 / 2026-10-10)

Only public HTTPS JSON feeds are read. No keys, authentication, HTML scraping, cookies, or AI traffic inspection. The application sends five requests per refresh (OpenAI summary and full component catalog, Claude summary, Google catalog and history), or six when OpenAI summary omits incidents. A shared HttpClient, cancellation and bounded 4 MiB response reads are used. A linked timeout covers response-body reads as well as headers. The 2026-10-10 source changes are included in Windows 2.4.1, published from merged main `0d69a64`; see [the deployment record](MAINTENANCE_2_4_1.md). Atom/RSS comparison is out of scope for this change.

## OpenAI and Claude

- [OpenAI summary](https://status.openai.com/api/v2/summary.json)
- [OpenAI full components](https://status.openai.com/api/v2/components.json)
- [Claude summary](https://status.claude.com/api/v2/summary.json)

Both summaries provide `components` and `status.indicator`; Claude also provides unresolved `incidents`. OpenAI sometimes omits `incidents` entirely (observed in a second live check on 2026-09-19). In that case the app additionally fetches [OpenAI incident history](https://status.openai.com/api/v2/incidents.json), validates the incidents array, and evaluates only active incidents. Missing or malformed history is UNKNOWN, not an empty healthy list. The suggested OpenAI `/api/v2/incidents/unresolved.json` returned HTTP 404 and is not used. The overall indicator is validated, but never used to lower all providers or unrelated Agent components.

OpenAI scope: ChatGPT/Work/conversations, Agent, Deep Research, Files/file uploads, Search, Login, Connectors/Apps, and Codex (Web, Desktop, API, VS Code extension and CLI). Current stable IDs plus semantic names handle ordinary renames and newly introduced IDs. Names explicitly identifying unrelated Sora, voice, images, realtime, embeddings, fine-tuning, FedRAMP, ads or billing-only problems are excluded. Both currently exposed `Login` IDs are included; the same name is used for API and ChatGPT login, so this remains a documented scope ambiguity. The 2026-10-10 full catalog also exposes `ChatGPT Work` explicitly.

The full catalog supplements components missing from summary; summary-only IDs are retained. Identical ID/name/status observations are deduplicated. If an ID's name or status differs between responses, both observations are evaluated, so an operational observation cannot erase a confirmed outage. Unknown status values retain uncertainty. Neither response is assumed newer from its retrieval order or page-wide `updated_at`.

Current matching entries (2026-10-10, public component names): `Files`, both `Login` entries, `Conversations`, `ChatGPT Work`, `Codex in ChatGPT Desktop`, `Search`, `File uploads`, `Deep Research`, `Agent`, `Connectors/Apps`, `Codex Web`, `Codex API`, `CLI`, `VS Code extension` (15 entries). The matching rules are not a hard-coded name-only list. Compliance API, general API endpoints, GPTs, dots, Space and Sites are not newly added to the scope. An unlinked incident concerning one of these cannot automatically be attributed to or excluded from the selected components.

Claude scope: `claude.ai`, `Claude API (api.anthropic.com)`, `Claude Code`, `Claude Cowork`, and their verified IDs. Console and Government-only incidents/components are excluded.

Incident component links take priority over a broad page roll-up. If links are absent, explicit related service names in the title can establish scope. Unscoped material incidents produce UNKNOWN, not a provider-wide outage or a false GO. Pure billing incidents do not imply Agent availability failure: the live OpenAI summary on verification day had an Agent API container overbilling incident with `impact: none` and no component links.

Version 2.0.1 adds a narrow OpenAI-only title fallback for the confirmed phrase `Plus and Pro users` (case-insensitive, whitespace-tolerant, word boundaries). The 2026-09-22 official incident had minor impact and no component links, while all summary components remained operational. This verified phrase is attributed to ChatGPT Plus/Pro and maps to DEGRADED / HOLD during FULL. Generic Plus/Pro words are not sufficient. Explicitly unrelated links and voice/images/Sora/billing-only exclusions still take precedence.

## Response receipt, assessment and historical status (2026-10-10 source change)

All required requests must return successful HTTP status and complete their bounded body reads for receipt success. Only then does `LastSuccessfulCheckUtc` advance, including a response whose JSON/schema/scope cannot be assessed. This timestamp is a receipt timestamp, not proof of GO or server-data freshness. HTTP errors, timeouts, cancellation, interrupted/oversized reads do not advance it. Missing-incident fallback is required when the original valid summary lacks `incidents`; a malformed payload is never replaced with a fabricated healthy list.

Unscoped incidents are stored separately in `UncertainIncidentId/Title` with `AssessmentIssue`; confirmed related incident fields stay empty. During FULL THROTTLE, received but unassessable data means UNKNOWN / CHECK, not HOLD/STOP or GO. A confirmed degraded component/incident takes HOLD; partial/major outage takes STOP, even alongside a separately displayed uncertain incident. During BURGER TIME, existing BURGER TIME / BURGER + CHECK / BURGER + ISSUE rules remain. Repeated receipt of uncertain data stays fresh CHECK; no successful receipt for 15 minutes becomes STALE / CHECK (or BURGER + CHECK), and no prior receipt remains UNKNOWN.

Cards show the last receipt time. Details separately show the latest receipt failure, assessment issue, last assessable status with its own time, confirmed incidents and uncertain incidents. On timeout/failure or aging, old confirmed incident titles are removed from the current card; the past assessed status is historical, never proof of a continuing outage. A recovered assessable response replaces/clears the diagnostic fields.

If the historical assessed timestamp is missing (including legacy cache values), its status is labeled `(시각 미상)`; no time is inferred. If that timestamp is the same instant as the displayed receipt timestamp, only the historical status is shown on that line. Different timestamps remain explicit. An assessment issue identical to the current judgment is displayed only in the current judgment line.

SQLite schema remains version 2. One auxiliary `AppMetadata` entry per provider (`ProviderStatusDiagnostics.v1.<Provider>`, maximum 8 KiB) preserves current diagnostics and the last assessable timestamp in the same transaction as the existing status cache. Only metadata matching that cache's `CheckedAtUtc` is read. Invalid/oversized/old auxiliary metadata is ignored. Legacy UNKNOWN/STALE incident fields are separated on read without rewriting historical rows or inventing the earlier assessment time. No credentials or personal quota values are stored in this diagnostic entry.

Provider cards also open fixed official status homepages on left click, via the Windows default HTTPS application: OpenAI `https://status.openai.com/`, Claude `https://status.claude.com/`, Gemini `https://www.google.com/appsstatus/dashboard/`. Feed-derived URLs are never shell-executed. Right click keeps the measurement menu.

Component mapping: operational → OPERATIONAL; degraded_performance / under_maintenance → DEGRADED; partial_outage → PARTIAL_OUTAGE; major_outage → MAJOR_OUTAGE. Active relevant incident severity minor/none → DEGRADED, major → PARTIAL_OUTAGE, critical → MAJOR_OUTAGE. These incident severity mappings are this application's normalization policy, not identical provider enum names. Active relevant incidents can lower an otherwise operational component; resolved/postmortem/completed incidents do not. Unknown enum values or missing required structure produce UNKNOWN. A known scoped outage remains visible even if some other field is unknown.

## Google Gemini

The [official Workspace dashboard](https://www.google.com/appsstatus/dashboard/) directly publishes:

- [Product catalog](https://www.google.com/appsstatus/dashboard/products.json) and [schema](https://www.google.com/appsstatus/dashboard/products.schema.json)
- [Incident history](https://www.google.com/appsstatus/dashboard/incidents.json) and [schema](https://www.google.com/appsstatus/dashboard/incidents.schema.json)

Catalog shape: `{ products: [{ title, id, current_title? }] }`. Gemini is product ID `npdyhgECDJ6tB66MxXyo`; `Gemini Notebook` is separate and excluded. The live catalog resolves current Gemini IDs; the verified stable ID also survives marketing-title changes. Incident history is an array. `affected_products[].id` selects Gemini; legacy `service_key` is a fallback when product IDs are absent. Incidents for other Workspace products are excluded.

`begin` and `end` identify the incident interval. A past end means resolved even if historical `status_impact` still says outage. For an ongoing incident, `most_recent_update.status` takes priority over historical `status_impact`: AVAILABLE → OPERATIONAL; SERVICE_INFORMATION → DEGRADED (caution); SERVICE_DISRUPTION → PARTIAL_OUTAGE; SERVICE_OUTAGE → MAJOR_OUTAGE. The latter three mappings are the application's policy. Unexpected schema/status/date values produce UNKNOWN. A valid catalog with no active Gemini incident means no officially reported incident (OPERATIONAL), not a synthetic latency test or a guarantee of successful work.

This covers the dashboard's Gemini product only, not every Google AI/Vertex/Gemini API backend, region, account or model. Official dashboards can lag user experience. Feed timestamps record successful retrieval, not independent measurement of service availability. The parent monitor preserves the last successful retrieval timestamp and marks overdue information STALE; parsers never label an unparseable response as a successful healthy check.

## Tests

`ProviderStatusTests.Run()` exercises deterministic JSON fixtures and in-memory HTTP handlers: all providers healthy; component/incident outages; unrelated-product isolation; renamed IDs; malformed/missing/unknown fields; incident scope; billing-only incident exclusion; Google resolved/active/future incidents; metadata; offline/HTTP errors; timeout; cancellation; response size limit. No network or user credentials are needed for this suite.
