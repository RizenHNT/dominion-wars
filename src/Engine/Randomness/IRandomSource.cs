namespace DominionWars.Engine.Randomness
{

public interface IRandomSource
{
    ulong NextUInt64();

    int NextInt(int exclusiveMaximum);
}
}
