using DominionWars.Engine.Model;

namespace DominionWars.Engine.Command
{

public interface ICommandBuffer
{
    int AppliedCount { get; }

    void Execute(GameState state, IGameCommand command);
}
}
