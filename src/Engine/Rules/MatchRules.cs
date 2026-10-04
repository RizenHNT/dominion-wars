using System;

namespace DominionWars.Engine.Rules
{

/// <summary>Runtime-tunable match rules that are independent from card data.</summary>
public sealed class MatchRules
{
    public MatchRules(
        int handLimit = 8,
        int pioneerHandLimitBonus = 2,
        int pioneerOpponentPunishBonus = 1,
        int pioneerSelfPunishDiscount = 0,
        int maxPunishResponsesPerRound = 0)
    {
        if (handLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handLimit));
        }

        if (pioneerHandLimitBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pioneerHandLimitBonus));
        }

        if (pioneerOpponentPunishBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pioneerOpponentPunishBonus));
        }

        if (pioneerSelfPunishDiscount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pioneerSelfPunishDiscount));
        }

        if (maxPunishResponsesPerRound < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPunishResponsesPerRound));
        }

        HandLimit = handLimit;
        PioneerHandLimitBonus = pioneerHandLimitBonus;
        PioneerOpponentPunishBonus = pioneerOpponentPunishBonus;
        PioneerSelfPunishDiscount = pioneerSelfPunishDiscount;
        MaxPunishResponsesPerRound = maxPunishResponsesPerRound;
    }

    public int HandLimit { get; }
    public int PioneerHandLimitBonus { get; }

    /// <summary>
    /// 先驱威压：仅对方统领在场时，本方所有卡牌的有效惩罚值 +N
    /// （对应 data/balance.json 的 pioneerOpponentPunishBonus，默认 1）。
    /// </summary>
    public int PioneerOpponentPunishBonus { get; }

    /// <summary>
    /// 先驱威压备选配置：仅本方统领在场时，本方自己的卡牌有效惩罚值 −N
    /// （对应 data/balance.json 的 pioneerSelfPunishDiscount，默认 0）。
    /// </summary>
    public int PioneerSelfPunishDiscount { get; }

    /// <summary>
    /// T1「每轮惩罚响应上限」：**一个根动作事件（root action event）**内的惩罚
    /// 响应链最多接受 N 次响应；达到上限后，同一根事件内剩余的可响应牌不再被
    /// 询问、也不再被接受。
    /// <para>
    /// 一轮 = 同一个根动作事件的全部响应（一次出牌 / 一次 COMMIT / 一次 PULL
    /// 产生的惩罚抽牌及其整棵嵌套响应链），**不是**一次抽牌批、也不是一整回合；
    /// 同一回合里的两个根事件各自拥有独立额度。
    /// </para>
    /// <para>
    /// 0 = 不限制。0 是出厂默认值：规则保持关闭，行为与加该字段之前逐位一致。
    /// 1 = 每个根事件只接受一次响应（owner 尚未批准该规则变更，故默认关闭）。
    /// </para>
    /// 对应 data/balance.json 的 maxPunishResponsesPerRound（出厂 0）。
    /// </summary>
    public int MaxPunishResponsesPerRound { get; }
}
}
