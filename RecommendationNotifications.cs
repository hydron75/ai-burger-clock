namespace AiBurgerClock;

// Unknown/STALE are gaps in knowledge, not a new confirmed recommendation.
// Keep the last confirmed state across those gaps; reset it at Schedule boundaries.
internal sealed class RecommendationNotifications
{
    private readonly Dictionary<ProviderKind, Recommendation> confirmed = new();
    private AgentState? lastSchedule;

    internal bool Observe(ProviderKind provider, AgentState schedule, Recommendation current, bool enabled)
    {
        if (lastSchedule != schedule)
        {
            confirmed.Clear();
            lastSchedule = schedule;
        }
        if (schedule != AgentState.FullThrottle) return false;
        if (current is not (Recommendation.Go or Recommendation.Hold or Recommendation.Stop)) return false;
        bool notify = enabled && confirmed.TryGetValue(provider, out var previous) &&
            RecommendationPolicy.ShouldNotify(previous, current);
        confirmed[provider] = current;
        return notify;
    }
}
