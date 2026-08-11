using System;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Command
{

public sealed class LocalCommandBuffer : ICommandBuffer
{
    public int AppliedCount { get; private set; }

    public void Execute(GameState state, IGameCommand command)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        state.Mutate(command);
        AppliedCount++;
    }
}

internal sealed class DelegateGameCommand : IGameCommand
{
    private readonly Action<GameState> _mutation;

    public DelegateGameCommand(Action<GameState> mutation)
    {
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public void Apply(GameState state)
    {
        _mutation(state);
    }
}
}
