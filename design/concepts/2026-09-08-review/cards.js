window.DW_CARDS = [
  {
    "id": "flame_leader",
    "name": "烈焰皇·焚天",
    "faction": "烈焰帝国",
    "type": "MINION",
    "tags": [
      "统领"
    ],
    "punish": 0,
    "attack": 8,
    "health": 10,
    "keywords": [
      "圣盾"
    ],
    "leader": true,
    "leaderDef": {
      "winCondition": "ROYAL_CASTLE_BREAK",
      "vulnerabilities": [
        "DAMAGE"
      ],
      "winText": "击破王城即获胜",
      "enterEffects": [
        {
          "action": "DAMAGE",
          "target": "ALL_ENEMY_MINIONS",
          "amount": 2
        }
      ],
      "punishEffects": [
        {
          "action": "END_TURN"
        },
        {
          "action": "DAMAGE",
          "target": "ALL_ENEMY_MINIONS",
          "amount": 2
        }
      ]
    },
    "text": "登场：对所有敌方随从造成2点伤害。【惩罚】立即结束对方回合，并对所有敌方随从造成2点伤害。",
    "flavor": "焚尽八荒，唯朕独尊。",
    "guard": true
  },
  {
    "id": "flame_recruit",
    "name": "烈焰新兵",
    "faction": "烈焰帝国",
    "type": "MINION",
    "tags": [
      "士兵"
    ],
    "punish": 1,
    "attack": 2,
    "health": 1,
    "text": "",
    "flavor": "帝国的火种，从不熄灭。"
  },
  {
    "id": "flame_imp",
    "name": "火舌小鬼",
    "faction": "烈焰帝国",
    "type": "MINION",
    "tags": [
      "恶魔"
    ],
    "punish": 0,
    "attack": 1,
    "health": 2,
    "onPlayEffects": [
      {
        "action": "DAMAGE",
        "target": "ENEMY_FACE",
        "amount": 1
      }
    ],
    "text": "登场：对敌方统领造成1点伤害。"
  },
  {
    "id": "flame_charger",
    "name": "冲锋骑兵",
    "faction": "烈焰帝国",
    "type": "MINION",
    "tags": [
      "骑兵"
    ],
    "punish": 2,
    "attack": 3,
    "health": 2,
    "keywords": [
      "突袭"
    ],
    "text": "召唤当回合即可攻击。"
  },
  {
    "id": "flame_guard",
    "name": "熔岩护卫",
    "faction": "烈焰帝国",
    "type": "MINION",
    "tags": [
      "守卫"
    ],
    "punish": 2,
    "attack": 2,
    "health": 6,
    "keywords": [
      "嘲讽"
    ],
    "text": "敌方必须优先攻击本随从。"
  },
  {
    "id": "flame_punish_wrath",
    "name": "焚天之怒",
    "faction": "烈焰帝国",
    "type": "PUNISH",
    "tags": [
      "天罚"
    ],
    "punish": 5,
    "punishActivatable": true,
    "punishCost": 1,
    "punishCondition": "ENEMY_MINIONS_GE_1",
    "punishEffects": [
      {
        "action": "DAMAGE",
        "target": "ALL_ENEMY_MINIONS",
        "amount": 4
      }
    ],
    "text": "惩罚牌：无法主动使用。【惩罚·敌方场上至少1个随从】以惩罚1对所有敌方随从造成4点伤害。"
  },
  {
    "id": "sea_leader",
    "name": "深渊主宰·涛冥",
    "faction": "深海联盟",
    "type": "SPELL",
    "tags": [
      "统领"
    ],
    "punish": 0,
    "leader": true,
    "leaderDef": {
      "grantLife": 25,
      "winCondition": "OPP_DISCARD_TOTAL_GE",
      "vulnerabilities": [
        "DAMAGE"
      ],
      "winParam": 18,
      "winText": "对方累计弃牌达到18张（弃牌阶段的强制弃牌不计入）",
      "enterEffects": [
        {
          "action": "DRAW",
          "amount": 2
        }
      ],
      "punishEffects": [
        {
          "action": "CONVERT_PUNISH_TO_DISCARD"
        }
      ]
    },
    "text": "登场：赋予你25点生命（归零落败），抽2张牌。胜利：对方累计弃牌≥18张（效果与惩罚转化弃牌才计入）。【惩罚】对方当回合的惩罚值不再使你抽牌，而是转化为弃置其自己的手牌。",
    "flavor": "深渊不索取，深渊只是收回。"
  },
  {
    "id": "sea_crab",
    "name": "铁甲蟹",
    "faction": "深海联盟",
    "type": "MINION",
    "tags": [
      "甲壳"
    ],
    "punish": 1,
    "attack": 1,
    "health": 4,
    "keywords": [
      "嘲讽"
    ],
    "text": ""
  }
];
