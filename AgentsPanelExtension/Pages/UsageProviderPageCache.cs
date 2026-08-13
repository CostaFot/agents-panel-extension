using System;
using System.Collections.Concurrent;

namespace AgentsPanelExtension;

// One UsageProviderPage per ProviderId, shared by the hub and the dock so both navigate into the SAME
// instance and its held state survives (MarketExtension's MarketsPage reuse precedent). Lazy rather
// than eager because providers appear/disappear across emissions (demo flip, IsAvailable off). Never
// evicts — a page for a vanished provider is inert: with no ItemsChanged subscriber it holds no
// repository observer, so it costs nothing.
//
// Concurrent because GetItems runs on host threads and pool threads across two surfaces.
internal sealed class UsageProviderPageCache(UsageRepository repository)
{
    private readonly ConcurrentDictionary<string, UsageProviderPage> _pages = new(StringComparer.Ordinal);

    public UsageProviderPage GetPage(DomainUsageSnapshot snapshot) =>
        _pages.GetOrAdd(snapshot.ProviderId,
            _ => new UsageProviderPage(repository, snapshot.ProviderId, snapshot.ProviderDisplayName));
}
