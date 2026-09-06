using System;
using System.Collections.Generic;

namespace DominionWars.Unity.Runtime
{

/// <summary>
/// Small composition boundary between screen intents and the real bootstrap.
/// It resolves only immutable deck metadata and delegates match construction,
/// validation, and snapshot publication to RuntimeBootstrap/the engine.
/// </summary>
public sealed class RuntimeMatchSetupOrchestrator
{
    private readonly RuntimeBootstrap _bootstrap;

    public RuntimeMatchSetupOrchestrator(RuntimeBootstrap bootstrap)
    {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    }

    public RuntimeBootstrap Bootstrap => _bootstrap;
    public IReadOnlyList<RuntimeDeckOption> DeckOptions => _bootstrap.DeckOptions;

    public bool TryStartMatch(
        string player0DeckId,
        string player1DeckId,
        out string reasonKey)
    {
        // RuntimeBootstrap commits only after its candidate adapter has a
        // snapshot. Do not perform a second post-commit readiness check here:
        // returning false after Bootstrap succeeds would leave the new
        // adapter installed while the caller believes the start failed.
        return _bootstrap.TryStartSession(player0DeckId, player1DeckId, out reasonKey);
    }

    public void StopSession()
    {
        _bootstrap.StopSession();
    }
}
}
