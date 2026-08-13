using DominionWars.Engine.Model;

namespace DominionWars.Engine.Turns
{

/// <summary>
/// Register card/phase action resolvers independently from the turn route.
/// Returning false leaves the request fail-closed for a later registered handler.
/// </summary>
public interface ITurnActionHandler
{
    bool CanHandle(string phaseId, string actionType);

    GameActionResult Execute(GameState state, GameActionRequest request, TurnFlow flow);
}
}
