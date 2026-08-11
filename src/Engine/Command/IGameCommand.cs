using DominionWars.Engine.Model;

namespace DominionWars.Engine.Command
{

public interface IGameCommand
{
    void Apply(GameState state);
}
}
