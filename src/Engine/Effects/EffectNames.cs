using System;
using System.Collections.Generic;

namespace DominionWars.Engine.Effects
{

public static class EffectNames
{
    public const string Damage = "DAMAGE";
    public const string Heal = "HEAL";
    public const string Draw = "DRAW";
    public const string OppDraw = "OPP_DRAW";
    public const string DiscardOppRandom = "DISCARD_OPP_RANDOM";
    public const string DiscardDrawn = "DISCARD_DRAWN";
    public const string Destroy = "DESTROY";
    public const string Buff = "BUFF";
    public const string GrantKeyword = "GRANT_KEYWORD";
    public const string Summon = "SUMMON";
    public const string SummonLeader = "SUMMON_LEADER";
    public const string EndTurn = "END_TURN";
    public const string AddOppPunishTurn = "ADD_OPP_PUNISH_TURN";
    public const string AddSelfPunishTurn = "ADD_SELF_PUNISH_TURN";
    public const string ConvertPunishToDiscard = "CONVERT_PUNISH_TO_DISCARD";
    public const string ProtectTurn = "PROTECT_TURN";
    public const string Negate = "NEGATE";
    public const string NegateEnemyEffectsTurn = "NEGATE_ENEMY_EFFECTS_TURN";
    public const string SkipReshuffle = "SKIP_RESHUFFLE";
    public const string RestoreAttacks = "RESTORE_ATTACKS";
    public const string GainLife = "GAIN_LIFE";
    public const string LoseLife = "LOSE_LIFE";
    public const string DamageCastle = "DAMAGE_CASTLE";
    public const string WinGame = "WIN_GAME";

    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
    {
        Damage, Heal, Draw, OppDraw, DiscardOppRandom, DiscardDrawn,
        Destroy, Buff, GrantKeyword, Summon, SummonLeader, EndTurn,
        AddOppPunishTurn, AddSelfPunishTurn, ConvertPunishToDiscard,
        ProtectTurn, Negate, NegateEnemyEffectsTurn, SkipReshuffle,
        RestoreAttacks, GainLife, LoseLife, DamageCastle, WinGame,
    });
}
}
