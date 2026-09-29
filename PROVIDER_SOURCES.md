# Official provider sources (verified 2026-09-19; OpenAI follow-up 2026-09-22)

Only public HTTPS JSON feeds are read. No keys, authentication, HTML scraping, cookies, or AI traffic inspection. The application sends four requests per refresh (one OpenAI summary, one Claude summary, Google catalog and history), or five when OpenAI summary omits incidents. A shared HttpClient, cancellation and bounded 4 MiB response reads are used. A linked timeout covers response-body reads as well as headers.

## OpenAI and Claude

- [OpenAI summary](https://status.openai.com/api/v2/summary.json)
- [Claude summary](https://status.claude.com/api/v2/summary.json)

Both summaries provide `components` and `status.indicator`; Claude also provides unresolved `incidents`. OpenAI sometimes omits `incidents` entirely (observed in a second live check on 2026-09-19). In that case the app additionally fetches [OpenAI incident history](https://status.openai.com/api/v2/incidents.json), validates the incidents array, and evaluates only active incidents. Missing or malformed history is UNKNOWN, not an empty healthy list. The suggested OpenAI `/api/v2/incidents/unresolved.json` returned HTTP 404 and is not used. The overall indicator is validated, but never used to lower all providers or unrelated Agent components.

OpenAI scope: ChatGPT/Work/conversations, Agent, Deep Research, Files/file uploads, Search, Login, Connectors/Apps, and Codex (Web, Desktop, API, VS Code extension and CLI). Current stable IDs plus semantic names handle ordinary renames and newly introduced IDs. Names explicitly identifying unrelated Sora, voice, images, realtime, embeddings, fine-tuning, FedRAMP, ads or billing-only problems are excluded. Both currently exposed `Login` IDs are included because the summary does not provide their parent service; this is a documented scope ambiguity. ChatGPT Work currently has no distinct component and is covered by shared related capabilities and explicit incident titles.

Claude scope: `claude.ai`, `Claude API (api.anthropic.com)`, `Claude Code`, `Claude Cowork`, and their verified IDs. Console and Government-only incidents/components are excluded.

Incident component links take priority over a broad page roll-up. If links are absent, explicit related service names in the title can establish scope. Unscoped material incidents produce UNKNOWN, not a provider-wide outage or a false GO. Pure billing incidents do not imply Agent availability failure: the live OpenAI summary on verification day had an Agent API container overbilling incident with `impact: none` and no component links.

Version 2.0.1 adds a narrow OpenAI-only title fallback for the confirmed phrase `Plus and Pro users` (case-insensitive, whitespace-tolerant, word boundaries). The 2026-09-22 official incident had minor impact and no component links, while all summary components remained operational. This verified phrase is attributed to ChatGPT Plus/Pro and maps to DEGRADED / HOLD during FULL. Generic Plus/Pro words are not sufficient. Explicitly unrelated links and voice/images/Sora/billing-only exclusions still take precedence. Unscoped/unknown incidents retain their current IDs/titles for diagnosis without advancing the last successful check; transport failure does not relabel a previous incident as newly observed.

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
