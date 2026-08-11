using System;

namespace DominionWars.Engine.Randomness
{

/// <summary>Deterministic xoshiro256** generator seeded through SplitMix64.</summary>
public sealed class Xoshiro256StarStar : IRandomSource
{
    private ulong _state0;
    private ulong _state1;
    private ulong _state2;
    private ulong _state3;

    public Xoshiro256StarStar(ulong seed)
    {
        var splitMixState = seed;
        _state0 = NextSplitMix64(ref splitMixState);
        _state1 = NextSplitMix64(ref splitMixState);
        _state2 = NextSplitMix64(ref splitMixState);
        _state3 = NextSplitMix64(ref splitMixState);
    }

    public ulong NextUInt64()
    {
        var result = RotateLeft(_state1 * 5, 7) * 9;
        var temporary = _state1 << 17;

        _state2 ^= _state0;
        _state3 ^= _state1;
        _state1 ^= _state2;
        _state0 ^= _state3;
        _state2 ^= temporary;
        _state3 = RotateLeft(_state3, 45);

        return result;
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        var bound = (ulong)exclusiveMaximum;
        var threshold = unchecked((0UL - bound) % bound);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    private static ulong RotateLeft(ulong value, int shift)
    {
        return (value << shift) | (value >> (64 - shift));
    }

    private static ulong NextSplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
}
