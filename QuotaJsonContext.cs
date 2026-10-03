using System.Text.Json.Serialization;

namespace AiBurgerClock;

// Keep the existing quota-cache JSON shape without runtime reflection.
// The Apple SDK's trimming analysis requires explicit serialization metadata.
[JsonSerializable(typeof(QuotaCache))]
internal partial class QuotaJsonContext : JsonSerializerContext;
