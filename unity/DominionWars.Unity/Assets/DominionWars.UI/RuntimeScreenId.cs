namespace DominionWars.Unity.UI
{

/// <summary>
/// The presentation-level screens owned by the local runtime shell. This
/// enum carries no rule, phase, victory, or balance meaning.
/// </summary>
public enum RuntimeScreenId
{
    Boot,
    Title,
    MainMenu,
    MatchSetup,
    BattleLoading,
    Battle,
    Result,
}

/// <summary>
/// Stable UI intents emitted by screen controls. A host may decide how to
/// satisfy an intent; the screen flow never creates or resets an engine
/// session on its own.
/// </summary>
public enum RuntimeScreenIntent
{
    StartMatch,
    Back,
    Restart,
    ReturnToMenu,
}
}
