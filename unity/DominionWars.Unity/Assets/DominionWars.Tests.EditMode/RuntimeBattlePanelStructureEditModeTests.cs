#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Unity.Runtime;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeBattlePanelStructureEditModeTests
{
    [Test]
    public void MissingSnapshotFieldsAreAbsentFromTheProductionSurface()
    {
        var player = new RuntimePlayerSnapshot
        {
            PlayerId = "player_0",
            Life = 18,
            DeckCount = 4,
            HandCount = 2,
            FieldCount = 1,
            GraveyardCount = 3,
        };

        var playerText = RuntimeBattlePanelPresentationModel.BuildPlayerSection(player, true);
        var castleText = RuntimeBattlePanelPresentationModel.BuildCastleSummary(
            new RuntimeCastleSnapshot { Enabled = true, Health = 75 });

        Assert.That(playerText, Does.Not.Contain("Unavailable"));
        Assert.That(playerText, Does.Not.Contain("统领："));
        Assert.That(playerText, Does.Not.Contain("除外："));
        Assert.That(castleText, Does.Contain("生命 75"));
        Assert.That(castleText, Does.Not.Contain("BARRIER"));
        Assert.That(castleText, Does.Not.Contain("屏障"));
    }

    [Test]
    public void LeaderSummaryUsesCanonicalLeaderZoneAndCycleWinCount()
    {
        var player = new RuntimePlayerSnapshot
        {
            PlayerId = "player_0",
            CycleWinCount = 3,
            LeaderZone = new[]
            {
                new RuntimeCardSnapshot
                {
                    CardId = "flame_leader",
                    EntityId = 41,
                    Sealed = true,
                },
            },
        };

        var text = RuntimeBattlePanelPresentationModel.BuildPlayerSection(player, true);
        var debugText = RuntimeBattlePanelPresentationModel.BuildDebugPlayerSection(player, true);

        Assert.That(text, Does.Contain("名称 统领"));
        Assert.That(text, Does.Not.Contain("flame_leader"));
        Assert.That(text, Does.Not.Contain("#41"));
        Assert.That(text, Does.Not.Contain("Unavailable"));
        Assert.That(text, Does.Contain("状态 sealed"));
        Assert.That(text, Does.Contain("洗牌胜利计数 3"));
        Assert.That(debugText, Does.Contain("名称 flame_leader#41"));
        Assert.That(debugText, Does.Contain("生命 Unavailable"));
    }

    [Test]
    public void LeaderStatusShowsPublicLandmarkAndChantProgress()
    {
        var player = new RuntimePlayerSnapshot
        {
            PlayerId = "player_0",
            LeaderZone = new[]
            {
                new RuntimeCardSnapshot
                {
                    CardId = "machine_leader",
                    EntityId = 42,
                    LandmarkPullCount = 2,
                    ChantRemaining = 1,
                },
            },
        };

        var status = RuntimeBattlePanelPresentationModel.BuildLeaderStatus(player);

        Assert.That(status, Does.Contain("present"));
        Assert.That(status, Does.Contain("地标层数 2"));
        Assert.That(status, Does.Contain("吟唱剩余 1"));
    }

    [Test]
    public void CastleSummaryUsesTheCorrespondingOwnAndOpponentCycleWinCounts()
    {
        var text = RuntimeBattlePanelPresentationModel.BuildCastleSummary(
            new RuntimeCastleSnapshot { Enabled = true, Health = 61 },
            new RuntimePlayerSnapshot { PlayerId = "player_0", CycleWinCount = 2 },
            new RuntimePlayerSnapshot { PlayerId = "player_1", CycleWinCount = 5 });

        Assert.That(text, Does.Contain("生命 61"));
        Assert.That(text, Does.Not.Contain("BARRIER"));
        Assert.That(text, Does.Not.Contain("屏障"));
        Assert.That(text, Does.Contain("洗牌胜利计数 己方 2 / 对手 5"));
    }

    [Test]
    public void PanelShowsIndependentLeaderSlotsAndHidesAbsentExileState()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                Life = 18,
                DeckCount = 4,
                HandCount = 2,
                FieldCount = 1,
                GraveyardCount = 3,
                CycleWinCount = 2,
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot { CardId = "own_leader", EntityId = 31, Sealed = false },
                },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                Life = 20,
                DeckCount = 6,
                HandCount = 4,
                FieldCount = 2,
                GraveyardCount = 1,
                CycleWinCount = 5,
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot { CardId = "opponent_leader", EntityId = 32, Sealed = true },
                },
            });
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelStructureTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var ownLeader = Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot");
            var opponentLeader = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentLeaderSlot");
            Assert.That(ownLeader, Is.Not.Null);
            Assert.That(opponentLeader, Is.Not.Null);
            Assert.That(ownLeader!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("own_leader#31"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("统领"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("STATUS  present"));
            Assert.That(opponentLeader!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("opponent_leader#32"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("统领"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("STATUS  sealed"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));

            RuntimeBattlePanelView.SetLeaderSlot(ownLeader, "Future Leader", "75", "present");
            var futureLeaderText = ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(futureLeaderText, Has.Some.Property("text").EqualTo("Future Leader\nLIFE  75"));
            Assert.That(futureLeaderText, Has.Some.Property("text").EqualTo("STATUS  present"));

            RuntimeBattlePanelView.SetLeaderSlot(
                ownLeader,
                "Machine Landmark",
                string.Empty,
                "present | 地标层数 2 · 吟唱剩余 1",
                "累计下载达成目标");
            var progressLeaderText = ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(progressLeaderText, Has.Some.Property("text")
                .EqualTo("目标  累计下载达成目标\n地标层数 2 · 吟唱剩余 1"));

            var ownExile = FindAny(panelObject, "RuntimeBattlePanelRightRail/OwnExile");
            var opponentExile = FindAny(panelObject, "RuntimeBattlePanelLeftRail/OpponentExile");
            Assert.That(ownExile, Is.Not.Null);
            Assert.That(opponentExile, Is.Not.Null);
            Assert.That(ownExile!.gameObject.activeSelf, Is.False);
            Assert.That(opponentExile!.gameObject.activeSelf, Is.False);
            Assert.That(ownExile.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));
            Assert.That(opponentExile.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));

            var castle = Find(panelObject, "RuntimeBattlePanelCenter/RuntimeBattlePanelCastle");
            Assert.That(castle, Is.Not.Null);
            Assert.That(castle!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("WIN COUNT OWN  2 / OPPONENT  5"));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void PublicLeaderSlotsShowCatalogNameAndGoalWithoutRevealingOpponentHand()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot { CardId = "own_leader", EntityId = 41, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                HandCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot
                    {
                        CardId = "opponent_hidden_leader",
                        EntityId = 42,
                        OwnerPlayer = 1,
                    },
                },
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot { CardId = "opponent_leader", EntityId = 43, OwnerPlayer = 1 },
                },
            });
        var catalog = new CardCatalog(new Dictionary<string, CardDefinition>(StringComparer.Ordinal)
        {
            ["own_leader"] = Leader("own_leader", "Own Leader", "己方封印随从生命≥512时获胜"),
            ["opponent_leader"] = Leader("opponent_leader", "Opponent Leader", "对方累计弃牌达到18张（弃牌阶段的强制弃牌不计入）"),
            ["opponent_hidden_leader"] = Leader("opponent_hidden_leader", "Hidden Leader", "隐藏统领目标"),
        });
        RuntimeContentResolver.TryLoad(
            "missing-leader-content-" + Guid.NewGuid().ToString("N"),
            out var resolver,
            false);
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelLeaderGoalTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            panel.BindContentResolver(resolver, catalog);

            var ownLeader = Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot");
            var opponentLeader = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentLeaderSlot");
            Assert.That(ownLeader, Is.Not.Null);
            Assert.That(opponentLeader, Is.Not.Null);
            Assert.That(ownLeader!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("目标  己方封印随从生命≥512时获胜"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("Own Leader"));
            Assert.That(opponentLeader!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("Opponent Leader"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("目标  对方累计弃牌达到18张"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("NAME  "));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("强制弃牌不计入"));
            Assert.That(panelObject.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("隐藏统领目标"));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void TerminalMachineGoalUsesWinnerPublicMetadataWithoutLeakingReasonToken()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot
                    {
                        CardId = "machine_leader",
                        EntityId = 71,
                        OwnerPlayer = 1,
                    },
                },
            });
        snapshot.Phase = "OVER";
        snapshot.WinnerPlayerIndex = 1;
        snapshot.ReasonKey = "win.pull_total_ge";

        var catalog = new CardCatalog(new Dictionary<string, CardDefinition>(StringComparer.Ordinal)
        {
            ["machine_leader"] = Leader(
                "machine_leader",
                "上古咒文·赋值机身",
                "己方累计完成6次下载"),
        });

        var resultText = RuntimeBattlePanelPresentationModel.BuildPlayerFacingResultReason(
            snapshot,
            catalog);

        Assert.That(resultText, Does.Contain("己方累计完成6次下载"));
        Assert.That(resultText, Does.Not.Contain("win.pull_total_ge"));
    }

    [TestCase("win.enemy_leader_defeated", "ENEMY LEADER DEFEATED")]
    [TestCase("win.enemy_life_zero", "ENEMY LIFE REACHED ZERO")]
    [TestCase("win.castle_break_minion", "CASTLE BREAK RESOLVED")]
    [TestCase("win.royal_castle_break", "ROYAL CASTLE BROKEN")]
    [TestCase("win.deck_cycles", "DECK CYCLE LIMIT REACHED")]
    [TestCase("win.gate_of_fate", "GATE OF FATE TRIGGERED")]
    [TestCase("win.special", "SPECIAL CONDITION MET")]
    [TestCase("win.opp_discard_total_ge", "OPPONENT DISCARD GOAL REACHED")]
    [TestCase("win.no_damage_turns_ge", "NO-DAMAGE TURN GOAL REACHED")]
    [TestCase("win.opp_punish_draw_turn_ge", "PUNISH-DRAW GOAL REACHED")]
    [TestCase("win.giant_health_ge", "GIANT HEALTH GOAL REACHED")]
    [TestCase("win.pull_total_ge", "PULL TOTAL GOAL REACHED")]
    public void ApprovedTerminalReasonsUseDistinctPlayerFacingCopy(
        string reasonKey,
        string expectedCue)
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.Phase = "OVER";
        snapshot.WinnerPlayerIndex = 0;
        snapshot.ReasonKey = reasonKey;

        var resultText = RuntimeBattlePanelPresentationModel.BuildPlayerFacingResultReason(
            snapshot,
            null!);

        Assert.That(resultText, Does.Contain(expectedCue));
        Assert.That(resultText, Does.Not.Contain(reasonKey));
        Assert.That(resultText, Does.Not.Contain("win."));
    }

    [Test]
    public void UnknownTerminalReasonUsesNeutralFallbackWithoutProtocolToken()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.Phase = "OVER";
        snapshot.WinnerPlayerIndex = 0;
        snapshot.ReasonKey = "win.future_rule_not_yet_mapped";

        var resultText = RuntimeBattlePanelPresentationModel.BuildPlayerFacingResultReason(
            snapshot,
            null!);

        Assert.That(resultText, Is.EqualTo("MATCH COMPLETE"));
        Assert.That(resultText, Does.Not.Contain("win.future_rule_not_yet_mapped"));
    }

    [Test]
    public void NonCardLegalTargetsUseExactStableRootsInsteadOfMechanicalRoot()
    {
        var legal = new[]
        {
            LegalAction("attack_leader", "ATTACK", "leader_1"),
            LegalAction("attack_player", "ATTACK", "player_1"),
            LegalAction("attack_castle", "ATTACK", "castle"),
            LegalAction("attack_external", "ATTACK", "external_target"),
        };
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.LegalActions = legal;
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelTargetTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var mechanical = FindAny(panelObject, "RuntimeBattlePanelRightRail/MechanicalCloudCommit");
            Assert.That(mechanical, Is.Not.Null);
            Assert.That(mechanical!.GetComponents<RuntimeBattleDropZone>(), Is.Empty);

            var opponentLeader = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentLeaderSlot");
            Assert.That(opponentLeader, Is.Not.Null);
            Assert.That(opponentLeader!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("leader_1"));
            var opponent = Find(panelObject, "RuntimeBattlePanelOpponent");
            Assert.That(opponent, Is.Not.Null);
            Assert.That(opponent!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("player_1"));
            Assert.That(opponent!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("DROP HERE"));
            Assert.That(opponent.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("Opponent Side"));
            Assert.That(opponent.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("target="));

            var castle = Find(panelObject, "RuntimeBattlePanelCenter/RuntimeBattlePanelCastle");
            Assert.That(castle, Is.Not.Null);
            Assert.That(castle!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("castle"));

            var targetLayer = Find(panelObject, "LegalTargetSurfaces");
            Assert.That(targetLayer, Is.Not.Null);
            var external = targetLayer!.Find("LegalTarget_external_target");
            Assert.That(external, Is.Not.Null);
            Assert.That(external!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("external_target"));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void LegacyEngineCastleTargetAliasStillMapsToCastleRoot()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.LegalActions = new[]
        {
            LegalAction("attack_castle_legacy", "ATTACK", "core:shared_castle"),
        };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelCastleAliasTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var castle = Find(panelObject, "RuntimeBattlePanelCenter/RuntimeBattlePanelCastle");
            Assert.That(castle, Is.Not.Null);
            Assert.That(castle!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("core:shared_castle"));
            var targetLayer = Find(panelObject, "LegalTargetSurfaces");
            Assert.That(targetLayer!.Find("LegalTarget_core_shared_castle"), Is.Null);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void RefreshRemovesStaleSemanticLeaderDropZonesBeforeRebuilding()
    {
        var snapshotA = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshotA.LegalActions = new[]
        {
            LegalAction("attack_old_leader", "ATTACK", "leader_1"),
        };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshotA));
        adapter.AcceptSnapshot(snapshotA);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelRefreshTargetTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var opponentLeader = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentLeaderSlot");
            Assert.That(opponentLeader, Is.Not.Null);
            Assert.That(opponentLeader!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("leader_1"));

            var snapshotB = Snapshot(
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" });
            snapshotB.SnapshotRevision = 2;
            snapshotB.LegalActions = new[]
            {
                LegalAction("attack_new_leader", "ATTACK", "leader_0"),
            };
            snapshotB.LegalActions[0].SnapshotRevision = 2;
            adapter.AcceptSnapshot(snapshotB);
            panel.Refresh();

            Assert.That(opponentLeader.GetComponents<RuntimeBattleDropZone>(), Is.Empty);
            Assert.That(opponentLeader.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False);

            var ownLeader = Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot");
            Assert.That(ownLeader, Is.Not.Null);
            Assert.That(ownLeader!.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("leader_0"));
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void GenericPromptTargetsDoNotOverlapOwnOrOpponentCardLanes()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                FieldCount = 1,
                Field = new[]
                {
                    new RuntimeCardSnapshot { CardId = "own_field", EntityId = 11, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                FieldCount = 1,
                Field = new[]
                {
                    new RuntimeCardSnapshot { CardId = "opponent_field", EntityId = 12, OwnerPlayer = 1 },
                },
            });
        snapshot.LegalActions = new[]
        {
            LegalAction("attack_prompt_alpha", "ATTACK", "prompt_alpha"),
            LegalAction("attack_prompt_beta", "ATTACK", "prompt_beta"),
        };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject canvasObject = null!;

        try
        {
            canvasObject = new GameObject(
                "RuntimeBattlePanelPromptLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var canvasRoot = canvasObject.GetComponent<RectTransform>();
            canvasRoot.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRoot.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRoot.pivot = new Vector2(0.5f, 0.5f);
            canvasRoot.sizeDelta = new Vector2(1280f, 720f);

            var panelObject = new GameObject("RuntimeBattlePanelPromptLayout", typeof(RectTransform));
            panelObject.transform.SetParent(canvasRoot, false);
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            Canvas.ForceUpdateCanvases();

            var targetLayer = Find(panelObject, "LegalTargetSurfaces");
            Assert.That(targetLayer, Is.Not.Null);
            var alpha = targetLayer!.Find("LegalTarget_prompt_alpha") as RectTransform;
            var beta = targetLayer.Find("LegalTarget_prompt_beta") as RectTransform;
            Assert.That(alpha, Is.Not.Null);
            Assert.That(beta, Is.Not.Null);
            Assert.That(alpha!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(beta!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(alpha.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("prompt_alpha"));
            Assert.That(beta.GetComponents<RuntimeBattleDropZone>(),
                Has.Some.Property("TargetId").EqualTo("prompt_beta"));

            var ownField = Find(panelObject, "RuntimeBattlePanelOwn/OwnField");
            var opponentField = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentField");
            Assert.That(ownField, Is.Not.Null);
            Assert.That(opponentField, Is.Not.Null);
            Assert.That(Overlaps(alpha, ownField!), Is.False);
            Assert.That(Overlaps(alpha, opponentField!), Is.False);
            Assert.That(Overlaps(beta, ownField!), Is.False);
            Assert.That(Overlaps(beta, opponentField!), Is.False);

            UnityEngine.Object.DestroyImmediate(panelObject);
        }
        finally
        {
            adapter.Dispose();
            if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void UntargetedPlayCardUsesExactAdvertisedActionOnOwnFieldDropSurface()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot { CardId = "playable_card", EntityId = 21, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        var play = LegalAction("play_21", "PLAY_CARD", null!);
        play.SourceId = 21L;
        play.CardId = "playable_card";
        snapshot.LegalActions = new[] { play };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject(
                "RuntimeBattlePanelUntargetedPlayTest",
                typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var ownField = Find(panelObject, "RuntimeBattlePanelOwn/OwnField");
            var dropSurface = Find(panelObject, "RuntimeBattlePanelOwn/OwnFieldDropSurface");
            Assert.That(ownField, Is.Not.Null);
            Assert.That(dropSurface, Is.Not.Null);
            Assert.That(ownField!.GetComponent<UnityEngine.UI.Image>(), Is.Null,
                "The card strip itself must not become a full-row drop target.");
            Assert.That(dropSurface!.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            Assert.That(dropSurface.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(dropSurface.GetSiblingIndex(), Is.LessThan(ownField.GetSiblingIndex()));
            var zones = dropSurface.GetComponents<RuntimeBattleDropZone>();
            Assert.That(zones, Has.Length.EqualTo(1));
            Assert.That(zones[0].TargetId, Is.Null);
            Assert.That(zones[0].ActionId, Is.EqualTo("play_21"));
            Assert.That(zones[0].ActionType, Is.EqualTo("PLAY_CARD"));

            var card = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(card, Is.Not.Null);
            var drag = card!.GetComponent<RuntimeBattleCardDrag>();
            Assert.That(drag, Is.Not.Null);
            Assert.That(drag!.CanAccept(null, "play_21", "PLAY_CARD"), Is.True);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void AmbushCardUsesDedicatedDropSurfaceAndOpponentAmbushStaysFaceDown()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                AmbushCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot { CardId = "new_ambush", EntityId = 31, OwnerPlayer = 0 },
                },
                Ambush = new[]
                {
                    new RuntimeCardSnapshot { CardId = "set_ambush", EntityId = 32, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_1",
                AmbushCount = 2,
            });
        snapshot.Phase = "AMBUSH";
        var set = LegalAction("set_ambush_31", "SET_AMBUSH", null!);
        set.SourceId = 31L;
        set.CardId = "new_ambush";
        snapshot.LegalActions = new[] { set };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelAmbushTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var ownZone = Find(panelObject, "RuntimeBattlePanelOwn/OwnAmbushZone");
            var dropSurface = Find(panelObject, "RuntimeBattlePanelOwn/OwnAmbushZone/OwnAmbushDropSurface");
            var ownCards = Find(panelObject, "RuntimeBattlePanelOwn/OwnAmbushZone/OwnAmbushZoneCards");
            var opponentCards = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentAmbushZone/OpponentAmbushZoneCards");
            Assert.That(ownZone, Is.Not.Null);
            Assert.That(dropSurface, Is.Not.Null);
            Assert.That(ownZone!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False,
                "The ambush frame must not cover its child cards with a drop hit.");
            Assert.That(dropSurface!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(dropSurface.GetComponents<RuntimeBattleDropZone>(), Has.Length.EqualTo(1));
            Assert.That(dropSurface.GetComponent<RuntimeBattleDropZone>().ActionType, Is.EqualTo("SET_AMBUSH"));
            Assert.That(ownCards, Is.Not.Null);
            Assert.That(ownCards!.Find("OwnCard_0"), Is.Not.Null,
                "The owner can inspect an already-set ambush.");
            Assert.That(opponentCards, Is.Not.Null);
            Assert.That(opponentCards!.childCount, Is.EqualTo(2));
            Assert.That(opponentCards.GetComponentsInChildren<RuntimeCardFaceView>(true), Is.Empty,
                "Opponent ambush identities stay hidden behind card backs.");

            var handCard = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(handCard, Is.Not.Null);
            var drag = handCard!.GetComponent<RuntimeBattleCardDrag>();
            Assert.That(drag, Is.Not.Null);
            Assert.That(drag!.CanAccept(null, "set_ambush_31", "SET_AMBUSH"), Is.True);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void CommitAndRollbackUseDedicatedEmptySpaceSurfaces()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                CommitQueueCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot { CardId = "rollback_card", EntityId = 41, OwnerPlayer = 0 },
                },
                CommitQueue = new[]
                {
                    new RuntimeCardSnapshot { CardId = "commit_card", EntityId = 42, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        var commit = LegalAction("commit_42", "COMMIT", null!);
        commit.SourceId = 42L;
        commit.CardId = "commit_card";
        var rollback = LegalAction("rollback_42", "ROLLBACK", null!);
        rollback.SourceId = 42L;
        rollback.CardId = "commit_card";
        snapshot.LegalActions = new[] { commit, rollback };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelQueueSurfaceTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var commitSurface = Find(panelObject, "RuntimeBattlePanelCenter/CommitDropSurface");
            var handSurface = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandDropSurface");
            var commitCards = Find(panelObject, "RuntimeBattlePanelCenter/CommitQueueCards");
            var hand = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp");
            Assert.That(commitSurface, Is.Not.Null);
            Assert.That(handSurface, Is.Not.Null);
            Assert.That(commitCards, Is.Not.Null);
            Assert.That(hand, Is.Not.Null);
            Assert.That(commitSurface!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(handSurface!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(commitSurface.GetComponent<RuntimeBattleDropZone>().ActionType, Is.EqualTo("COMMIT"));
            Assert.That(handSurface.GetComponent<RuntimeBattleDropZone>().ActionType, Is.EqualTo("ROLLBACK"));
            Assert.That(commitSurface.GetSiblingIndex(), Is.LessThan(commitCards!.GetSiblingIndex()));
            Assert.That(handSurface.GetSiblingIndex(), Is.LessThan(hand!.GetSiblingIndex()));
            Assert.That(commitCards.GetComponents<RuntimeBattleDropZone>(), Is.Empty);
            Assert.That(hand.GetComponents<RuntimeBattleDropZone>(), Is.Empty);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void EndTurnStaysPrimaryWhileOtherLegalActionsUseClosedMoreActionsDrawer()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        var endTurn = LegalAction("end_turn", "END_TURN", null!);
        var commit = LegalAction("commit_42", "COMMIT", null!);
        commit.SourceId = 42L;
        commit.CardId = "commit_card";
        snapshot.LegalActions = new[] { endTurn, commit };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelMoreActionsTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(panel.View, Is.Not.Null);
            Assert.That(panel.View.ActionsRoot.Find("Action_end_turn"), Is.Not.Null);
            Assert.That(panel.View.ActionsDrawerContent.Find("Action_commit_42"), Is.Not.Null);
            Assert.That(panel.View.MoreActionsButton, Is.Not.Null);
            Assert.That(panel.View.MoreActionsButton.gameObject.activeSelf, Is.True);
            Assert.That(panel.View.MoreActionsButton.interactable, Is.True);
            Assert.That(panel.View.ActionsDrawerRoot.gameObject.activeSelf, Is.False);

            panel.View.MoreActionsButton.onClick.Invoke();
            Assert.That(panel.View.ActionsDrawerRoot.gameObject.activeSelf, Is.True);
            panel.View.ActionsDrawerCloseButton.onClick.Invoke();
            Assert.That(panel.View.ActionsDrawerRoot.gameObject.activeSelf, Is.False);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void AmbushSkipUsesPhaseContextSurfaceAndDoesNotOpenMoreActions()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot { PlayerId = "player_0" },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.Phase = "AMBUSH";
        snapshot.LegalActions = new[] { LegalAction("skip_ambush", "SKIP_AMBUSH", null!) };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelPhaseContextTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(panel.View.PhaseActionsRoot.gameObject.activeSelf, Is.True);
            Assert.That(panel.View.PhaseActionsContent.Find("Action_skip_ambush"), Is.Not.Null);
            Assert.That(panel.View.ActionsRoot.Find("Action_skip_ambush"), Is.Null);
            Assert.That(panel.View.ActionsDrawerContent.Find("Action_skip_ambush"), Is.Null);
            Assert.That(panel.View.MoreActionsButton.gameObject.activeSelf, Is.False);
            Assert.That(panel.View.ActionsDrawerRoot.gameObject.activeSelf, Is.False);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void DiscardConfirmationUsesHandAdjacentContextSurface()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot
                    {
                        CardId = "discard_card",
                        EntityId = 77,
                        OwnerPlayer = 0,
                    },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.Phase = "DISCARD";
        var discard = LegalAction("discard_77", "DISCARD", null!);
        discard.SourceId = 77L;
        discard.CardId = "discard_card";
        snapshot.LegalActions = new[] { discard };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelDiscardContextTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(panel.View.DiscardActionsRoot.gameObject.activeSelf, Is.True);
            Assert.That(panel.View.DiscardActionsContent.Find("Action_discard_77"), Is.Not.Null);
            Assert.That(panel.View.ActionsRoot.Find("Action_discard_77"), Is.Null);
            Assert.That(panel.View.ActionsDrawerContent.Find("Action_discard_77"), Is.Null);

            var hand = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp");
            Assert.That(hand, Is.Not.Null);
            Assert.That(Overlaps(panel.View.DiscardActionsRoot, hand!), Is.False,
                "Discard confirmation must sit beside/above the hand, not cover the selected cards.");
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void MenuOpensPauseDrawerAndSettingsUsesOnlyTheRealReducedMotionToggle()
    {
        GameObject panelObject = null!;
        var returnedToMenu = false;
        try
        {
            panelObject = new GameObject("RuntimeBattlePanelPauseDrawerTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.RecoveryRequested += () => returnedToMenu = true;

            panel.RequestRecovery();
            Assert.That(panel.View.PauseDrawerRoot.gameObject.activeSelf, Is.True);
            Assert.That(panel.View.PauseContinueButton, Is.Not.Null);
            Assert.That(panel.View.PauseSettingsButton, Is.Not.Null);
            Assert.That(panel.View.PauseMainMenuButton, Is.Not.Null);
            Assert.That(panel.View.PauseSettingsRoot.gameObject.activeSelf, Is.False);

            panel.View.PauseSettingsButton.onClick.Invoke();
            Assert.That(panel.View.PauseSettingsRoot.gameObject.activeSelf, Is.True);
            Assert.That(panel.View.ReducedMotionToggle, Is.Not.Null);
            Assert.That(panel.View.ReducedMotionToggleRoot.parent, Is.EqualTo(panel.View.PauseSettingsRoot));
            Assert.That(panel.View.PauseSettingsRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").EqualTo("VOLUME"));
            Assert.That(panel.View.PauseSettingsRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").EqualTo("GRAPHICS"));
            Assert.That(panel.View.PauseSettingsRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").EqualTo("QUALITY"));

            panel.View.PauseSettingsBackButton.onClick.Invoke();
            Assert.That(panel.View.PauseSettingsRoot.gameObject.activeSelf, Is.False);
            Assert.That(panel.View.PauseDrawerRoot.gameObject.activeSelf, Is.True);

            panel.View.PauseContinueButton.onClick.Invoke();
            Assert.That(panel.View.PauseDrawerRoot.gameObject.activeSelf, Is.False);
            panel.RequestRecovery();
            panel.View.PauseMainMenuButton.onClick.Invoke();
            Assert.That(returnedToMenu, Is.True);
            Assert.That(panel.View.PauseDrawerRoot.gameObject.activeSelf, Is.False);
        }
        finally
        {
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void ExistingEventSystemWithoutInputModuleReceivesCompatibleModule()
    {
        GameObject eventSystemObject = null!;
        GameObject panelObject = null!;
        try
        {
            eventSystemObject = new GameObject(
                "RuntimeBattlePanelExistingEventSystemTest",
                typeof(UnityEngine.EventSystems.EventSystem));
            panelObject = new GameObject("RuntimeBattlePanelExistingEventSystemPanel", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Refresh();

            var eventSystem = eventSystemObject.GetComponent<UnityEngine.EventSystems.EventSystem>();
            Assert.That(eventSystemObject.GetComponents<UnityEngine.EventSystems.EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(eventSystemObject.GetComponents<UnityEngine.EventSystems.BaseInputModule>(),
                Has.Length.EqualTo(1));
            Assert.That(eventSystemObject.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>(),
                Is.Not.Null);
            Assert.That(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsSortMode.None), Has.Some.EqualTo(eventSystem));
        }
        finally
        {
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
            if (eventSystemObject != null) UnityEngine.Object.DestroyImmediate(eventSystemObject);
        }
    }

    [Test]
    public void ViewerMismatchClearsPrivateCardsTargetsAndActionsWithoutRemovingStaticSlots()
    {
        var snapshotA = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                FieldCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot { CardId = "private_old_card", EntityId = 71, OwnerPlayer = 0 },
                },
                Field = new[]
                {
                    new RuntimeCardSnapshot { CardId = "private_old_field", EntityId = 72, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshotA.LegalActions = new[]
        {
            LegalAction("attack_private_target", "ATTACK", "private_old_target"),
        };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshotA));
        adapter.AcceptSnapshot(snapshotA);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelPrivacyTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0"), Is.Not.Null);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnField/OwnCard_0"), Is.Not.Null);
            Assert.That(Find(panelObject, "LegalTargetSurfaces")!.Find("LegalTarget_private_old_target"),
                Is.Not.Null);
            Assert.That(panel.ActionGroups, Is.Not.Empty);

            var mismatch = Snapshot(
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" });
            mismatch.SnapshotRevision = 2;
            mismatch.ViewerPlayerId = "player_1";
            mismatch.LegalActions = Array.Empty<RuntimeLegalAction>();
            adapter.AcceptSnapshot(mismatch);
            panel.Refresh();

            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0"), Is.Null);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnField/OwnCard_0"), Is.Null);
            var targetLayer = Find(panelObject, "LegalTargetSurfaces");
            Assert.That(targetLayer, Is.Not.Null);
            Assert.That(targetLayer!.Find("LegalTarget_private_old_target"), Is.Null);
            Assert.That(panel.ActionGroups, Is.Empty);
            Assert.That(panelObject.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("private_old_"));
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot"), Is.Not.Null);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOpponent/OpponentLeaderSlot"), Is.Not.Null);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn")!
                .GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOpponent")!
                .GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void SnapshotRefreshFailureClearsStaleActionsAndExposesRecovery()
    {
        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                Hand = new[] { new RuntimeCardSnapshot { CardId = "stale_card", EntityId = 101, OwnerPlayer = 0 } },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshot.LegalActions = new[] { LegalAction("stale_action", "END_TURN", null!) };
        var session = new SnapshotSession(snapshot);
        var adapter = new RuntimeAdapter(session);
        adapter.AcceptSnapshot(snapshot);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject("RuntimeBattlePanelRecoveryTest", typeof(RectTransform));
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            Assert.That(panel.ActionGroups, Is.Not.Empty);

            session.ThrowOnGetSnapshot = true;
            panel.SetViewerPlayerIndex(1);

            Assert.That(panel.ActionGroups, Is.Empty);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0"), Is.Null);
            var recovery = panelObject.GetComponentInChildren<UnityEngine.UI.Button>(true);
            Assert.That(recovery, Is.Not.Null);
            Assert.That(recovery!.name, Is.EqualTo("RuntimeBattlePanelRecoveryButton"));
            Assert.That(recovery.interactable, Is.True);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    [Test]
    public void ViewerMismatchAndUnbindClearAllDynamicPresentationAndDisableRootInteraction()
    {
        var snapshotA = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = 1,
                FieldCount = 1,
                Hand = new[]
                {
                    new RuntimeCardSnapshot { CardId = "old_hand", EntityId = 81, OwnerPlayer = 0 },
                },
                Field = new[]
                {
                    new RuntimeCardSnapshot { CardId = "old_field", EntityId = 82, OwnerPlayer = 0 },
                },
                LeaderZone = new[]
                {
                    new RuntimeCardSnapshot { CardId = "old_leader", EntityId = 83, OwnerPlayer = 0 },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        snapshotA.LegalActions = new[]
        {
            LegalAction("old_target_action", "ATTACK", "old_target"),
        };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshotA));
        adapter.AcceptSnapshot(snapshotA);
        GameObject panelObject = null!;

        try
        {
            panelObject = new GameObject(
                "RuntimeBattlePanelMismatchUnbindPrivacyTest",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            var panelImage = panelObject.GetComponent<UnityEngine.UI.Image>();
            panelImage!.raycastTarget = true;
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);

            var ownLeader = Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot");
            Assert.That(ownLeader, Is.Not.Null);
            var dynamicLeader = new GameObject("OldLeaderCard", typeof(RectTransform));
            dynamicLeader.transform.SetParent(ownLeader!.transform, false);
            dynamicLeader.AddComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var targetLayer = Find(panelObject, "LegalTargetSurfaces");
            Assert.That(targetLayer, Is.Not.Null);
            Assert.That(targetLayer!.Find("LegalTarget_old_target"), Is.Not.Null);
            Assert.That(panelObject.GetComponentsInChildren<UnityEngine.UI.Button>(true), Is.Not.Empty);

            var mismatch = Snapshot(
                new RuntimePlayerSnapshot { PlayerId = "player_0" },
                new RuntimePlayerSnapshot { PlayerId = "player_1" });
            mismatch.SnapshotRevision = 2;
            mismatch.ViewerPlayerId = "player_1";
            adapter.AcceptSnapshot(mismatch);
            panel.Refresh();

            AssertUnavailablePresentation(panelObject, panel, panelImage);

            // Rebind a fresh valid snapshot so Unbind is tested with live
            // dynamic content rather than only an already-unavailable panel.
            var snapshotB = Snapshot(
                new RuntimePlayerSnapshot
                {
                    PlayerId = "player_0",
                    HandCount = 1,
                    Hand = new[]
                    {
                        new RuntimeCardSnapshot { CardId = "new_hand", EntityId = 91, OwnerPlayer = 0 },
                    },
                    LeaderZone = new[]
                    {
                        new RuntimeCardSnapshot { CardId = "new_leader", EntityId = 92, OwnerPlayer = 0 },
                    },
                },
                new RuntimePlayerSnapshot { PlayerId = "player_1" });
            snapshotB.SnapshotRevision = 3;
            snapshotB.LegalActions = new[]
            {
                LegalAction("new_target_action", "ATTACK", "new_target"),
            };
            snapshotB.LegalActions[0].SnapshotRevision = 3;
            adapter.AcceptSnapshot(snapshotB);
            panel.Refresh();
            panelImage.raycastTarget = true;
            var newLeader = Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot");
            Assert.That(newLeader, Is.Not.Null);
            var newDynamicLeader = new GameObject("NewLeaderCard", typeof(RectTransform));
            newDynamicLeader.transform.SetParent(newLeader!.transform, false);
            Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0"), Is.Not.Null);
            Assert.That(Find(panelObject, "LegalTargetSurfaces")!.Find("LegalTarget_new_target"), Is.Not.Null);

            panel.Unbind();

            AssertUnavailablePresentation(panelObject, panel, panelImage);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
        }
    }

    private static void AssertUnavailablePresentation(
        GameObject panelObject,
        RuntimeBattlePanel panel,
        UnityEngine.UI.Image panelImage)
    {
        Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0"), Is.Null);
        Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnField/OwnCard_0"), Is.Null);
        Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot/OldLeaderCard"), Is.Null);
        Assert.That(Find(panelObject, "RuntimeBattlePanelOwn/OwnLeaderSlot/NewLeaderCard"), Is.Null);
        var targetLayer = Find(panelObject, "LegalTargetSurfaces");
        Assert.That(targetLayer, Is.Not.Null);
        Assert.That(targetLayer!.childCount, Is.Zero);
        Assert.That(panelObject.GetComponentsInChildren<RuntimeBattleDropZone>(true), Is.Empty);
        var recovery = panelObject.GetComponentInChildren<UnityEngine.UI.Button>(true);
        Assert.That(recovery, Is.Not.Null);
        Assert.That(recovery!.name, Is.EqualTo("RuntimeBattlePanelRecoveryButton"));
        Assert.That(recovery.interactable, Is.True);
        Assert.That(panel.ActionGroups, Is.Empty);
        Assert.That(panelImage.raycastTarget, Is.False);
        Assert.That(Find(panelObject, "RuntimeBattlePanelOwn")!
            .GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False);
        Assert.That(Find(panelObject, "RuntimeBattlePanelOpponent")!
            .GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False);
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void CoreTabletopZonesRemainPositiveAndSeparated(float width, float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattlePanelStructureLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(width, height);
            var view = RuntimeBattlePanelView.Build(root);
            Canvas.ForceUpdateCanvases();
            var targetLayer = view.MainBattleRoot.Find("LegalTargetSurfaces") as RectTransform;
            Assert.That(targetLayer, Is.Not.Null);

            var zones = new[]
            {
                view.LeftRailRoot,
                view.MainBattleRoot,
                view.RightRailRoot,
                view.FooterRoot,
                view.OpponentLeaderRoot,
                view.OwnLeaderRoot,
                view.CastleRoot,
                view.CastleBarrierRoot,
                targetLayer!,
            };
            foreach (var zone in zones)
            {
                Assert.That(zone.rect.width, Is.GreaterThan(0f), zone.name + " width");
                Assert.That(zone.rect.height, Is.GreaterThan(0f), zone.name + " height");
            }

            Assert.That(Overlaps(view.LeftRailRoot, view.MainBattleRoot), Is.False);
            Assert.That(Overlaps(view.MainBattleRoot, view.RightRailRoot), Is.False);
            Assert.That(Overlaps(view.MainBattleRoot, view.FooterRoot), Is.False);
        }
        finally
        {
            if (rootObject != null) UnityEngine.Object.DestroyImmediate(rootObject);
        }
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void VisualLayersKeepSemanticDropSurfacesBehindCardsAndRailsSeparated(float width, float height)
    {
        GameObject rootObject = null!;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattlePanelLayerLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(width, height);
            var view = RuntimeBattlePanelView.Build(root);
            Canvas.ForceUpdateCanvases();

            Assert.That(view.TargetZonesRoot.GetSiblingIndex(),
                Is.LessThan(view.OpponentRoot.GetSiblingIndex()));
            Assert.That(view.OpponentRoot.GetSiblingIndex(),
                Is.LessThan(view.CenterRoot.GetSiblingIndex()));
            Assert.That(view.CenterRoot.GetSiblingIndex(),
                Is.LessThan(view.OwnRoot.GetSiblingIndex()));
            Assert.That(view.OpponentHandRoot.GetSiblingIndex(),
                Is.GreaterThan(view.OpponentFieldRoot.GetSiblingIndex()));
            Assert.That(view.OwnHandRoot.GetSiblingIndex(),
                Is.GreaterThan(view.OwnFieldRoot.GetSiblingIndex()));
            Assert.That(view.FeedbackRoot.GetSiblingIndex(),
                Is.EqualTo(view.FeedbackRoot.parent.childCount - 1));
            Assert.That(view.ActionsArea.GetSiblingIndex(),
                Is.GreaterThan(view.EventsArea.GetSiblingIndex()));

            var surfaces = new[]
            {
                view.OwnFieldDropSurface,
                view.OwnHandDropSurface,
                view.OwnAmbushDropSurface,
                view.CommitDropSurface,
            };
            foreach (var surface in surfaces)
            {
                Assert.That(surface.gameObject.activeSelf, Is.False, surface.name + " must be quiet without an action.");
                Assert.That(surface.rect.width, Is.GreaterThan(0f), surface.name + " width");
                Assert.That(surface.rect.height, Is.GreaterThan(0f), surface.name + " height");
                Assert.That(surface.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.False,
                    surface.name + " must not intercept input without an advertised action.");
            }

            Assert.That(view.OwnFieldDropSurface.GetSiblingIndex(),
                Is.LessThan(view.OwnFieldRoot.GetSiblingIndex()));
            Assert.That(view.OwnHandDropSurface.GetSiblingIndex(),
                Is.LessThan(view.OwnHandRoot.GetSiblingIndex()));
            Assert.That(view.OwnAmbushDropSurface.GetSiblingIndex(),
                Is.LessThan(view.OwnAmbushCardsRoot.GetSiblingIndex()));
            Assert.That(view.CommitDropSurface.GetSiblingIndex(),
                Is.LessThan(view.CommitCardsRoot.GetSiblingIndex()));

            Assert.That(view.OwnFieldDropSurface.rect.width,
                Is.EqualTo(view.OwnFieldRoot.rect.width).Within(0.5f));
            Assert.That(view.OwnFieldDropSurface.rect.height,
                Is.EqualTo(view.OwnFieldRoot.rect.height).Within(0.5f));
            Assert.That(view.OwnHandDropSurface.rect.width,
                Is.EqualTo(view.OwnHandRoot.rect.width).Within(0.5f));
            Assert.That(view.OwnHandDropSurface.rect.height,
                Is.EqualTo(view.OwnHandRoot.rect.height).Within(0.5f));
            Assert.That(view.CommitDropSurface.rect.width,
                Is.EqualTo(view.CommitCardsRoot.rect.width).Within(0.5f));
            Assert.That(view.CommitDropSurface.rect.height,
                Is.EqualTo(view.CommitCardsRoot.rect.height).Within(0.5f));
            Assert.That(Overlaps(view.HeaderRoot, view.BoardRoot), Is.False);
            Assert.That(Overlaps(view.BoardRoot, view.FooterRoot), Is.False);
            Assert.That(Overlaps(view.EventsArea, view.ActionsArea), Is.False);
        }
        finally
        {
            if (rootObject != null) UnityEngine.Object.DestroyImmediate(rootObject);
        }
    }

    [TestCase(1280f, 720f)]
    [TestCase(1440f, 900f)]
    public void HandCardsReserveReadableGeometryAndKeepLeadingEdgeRaycastable(float width, float height)
    {
        var handCards = new RuntimeCardSnapshot[6];
        for (var index = 0; index < handCards.Length; index++)
        {
            handCards[index] = new RuntimeCardSnapshot
            {
                CardId = "hand_card_" + index,
                EntityId = 101 + index,
                OwnerPlayer = 0,
            };
        }

        var snapshot = Snapshot(
            new RuntimePlayerSnapshot
            {
                PlayerId = "player_0",
                HandCount = handCards.Length,
                Hand = handCards,
                FieldCount = 1,
                Field = new[]
                {
                    new RuntimeCardSnapshot
                    {
                        CardId = "field_card",
                        EntityId = 201,
                        OwnerPlayer = 0,
                    },
                },
            },
            new RuntimePlayerSnapshot { PlayerId = "player_1" });
        var play = LegalAction("play_hand_card", "PLAY_CARD", null!);
        play.SourceId = 101L;
        play.CardId = "hand_card_0";
        snapshot.LegalActions = new[] { play };
        var adapter = new RuntimeAdapter(new SnapshotSession(snapshot));
        adapter.AcceptSnapshot(snapshot);
        GameObject canvasObject = null!;
        GameObject panelObject = null!;

        try
        {
            canvasObject = new GameObject(
                "RuntimeBattlePanelCardReadabilityCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var canvasRoot = canvasObject.GetComponent<RectTransform>();
            canvasRoot.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRoot.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRoot.pivot = new Vector2(0.5f, 0.5f);
            canvasRoot.sizeDelta = new Vector2(width, height);

            panelObject = new GameObject(
                "RuntimeBattlePanelCardReadability",
                typeof(RectTransform));
            panelObject.transform.SetParent(canvasRoot, false);
            panelObject.SetActive(false);
            var panel = panelObject.AddComponent<RuntimeBattlePanel>();
            panel.Bind(adapter);
            Canvas.ForceUpdateCanvases();

            var hand = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp");
            var field = Find(panelObject, "RuntimeBattlePanelOwn/OwnField");
            var card = Find(panelObject, "RuntimeBattlePanelOwn/OwnHandFaceUp/OwnCard_0");
            Assert.That(hand, Is.Not.Null);
            Assert.That(field, Is.Not.Null);
            Assert.That(card, Is.Not.Null);
            Assert.That(hand!.rect.height, Is.GreaterThanOrEqualTo(110f));
            Assert.That(field!.rect.height, Is.GreaterThanOrEqualTo(90f));
            Assert.That(card!.rect.height, Is.GreaterThanOrEqualTo(100f));

            var face = card.GetComponent<RuntimeCardFaceView>();
            Assert.That(face, Is.Not.Null);
            Assert.That(face!.CostBadge.gameObject.activeSelf, Is.True,
                "Player-facing card cost must not be diagnostics-only.");
            Assert.That(face.PunishValue.gameObject.activeSelf, Is.True);
            Assert.That(face.TitleText.fontSize, Is.GreaterThanOrEqualTo(12));
            Assert.That(card.GetComponent<UnityEngine.UI.Image>()!.raycastTarget, Is.True,
                "The whole card leading edge must remain an inspect/drag hit surface.");
            Assert.That(card.GetComponent<RuntimeBattleCardDrag>(), Is.Not.Null);
        }
        finally
        {
            adapter.Dispose();
            if (panelObject != null) UnityEngine.Object.DestroyImmediate(panelObject);
            if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void NormalViewHidesDiagnosticCaptionsAndDebugOverlayRemainsExplicit()
    {
        GameObject? rootObject = null;
        try
        {
            rootObject = new GameObject(
                "RuntimeBattlePanelSurfaceGateTest",
                typeof(RectTransform),
                typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root!.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(1280f, 720f);

            var view = RuntimeBattlePanelView.Build(root);
            Canvas.ForceUpdateCanvases();

            Assert.That(view.DebugOverlayRoot.gameObject.activeSelf, Is.False);
            AssertNoProductionWireTokens(rootObject);

            view.SetDebugOverlayVisible(true);
            Assert.That(
                view.DebugOverlayRoot.gameObject.activeSelf,
                Is.True,
                "Editor/Development diagnostics must remain explicitly reachable.");
            view.SetDebugOverlayVisible(false);
            Assert.That(view.DebugOverlayRoot.gameObject.activeSelf, Is.False);
        }
        finally
        {
            if (rootObject != null) UnityEngine.Object.DestroyImmediate(rootObject);
        }
    }

    private static void AssertNoProductionWireTokens(GameObject rootObject)
    {
        var internalTokens = new[]
        {
            "stable",
            "entity",
            "actionid",
            "source=",
            "target=",
            "reasonkey",
            "revision",
            "eventid",
            "parent=",
            "adapter order",
            "structure placeholder",
            "left rail",
            "right rail",
        };

        foreach (var label in rootObject.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            if (!label.gameObject.activeInHierarchy) continue;
            foreach (var token in internalTokens)
            {
                Assert.That(
                    label.text,
                    Does.Not.Contain(token).IgnoreCase,
                    label.name + " leaked production token: " + token);
            }
        }
    }

    private static RuntimeSnapshotEnvelope Snapshot(
        RuntimePlayerSnapshot own,
        RuntimePlayerSnapshot opponent)
    {
        return new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "match_structure_test",
            SnapshotRevision = 1,
            Turn = 2,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[] { own, opponent },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 61 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
    }

    private static CardDefinition Leader(string id, string name, string winText)
    {
        return new CardDefinition(
            id,
            name,
            attack: 1,
            health: 5,
            isMinion: true,
            isLeader: true,
            faction: "古木圣地",
            type: "MINION",
            leaderWinText: winText);
    }

    private static RuntimeLegalAction LegalAction(string actionId, string type, object targetId)
    {
        return new RuntimeLegalAction
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            SnapshotRevision = 1,
            ActionId = actionId,
            Type = type,
            Actor = 0,
            TargetId = targetId,
            Payload = new Dictionary<string, object?>(),
        };
    }

    private static RectTransform? Find(GameObject root, string path)
    {
        var current = root.transform.Find("RuntimeBattlePanelContent/RuntimeBattlePanelBoard/RuntimeBattlePanelMainBattle/" + path);
        return current as RectTransform;
    }

    private static RectTransform? FindAny(GameObject root, string path)
    {
        var current = Find(root, path);
        if (current != null) return current;
        return root.transform.Find("RuntimeBattlePanelContent/RuntimeBattlePanelBoard/" + path) as RectTransform;
    }

    private static bool Overlaps(RectTransform left, RectTransform right)
    {
        var a = left.rect;
        var b = right.rect;
        var leftCorners = new Vector3[4];
        var rightCorners = new Vector3[4];
        left.GetWorldCorners(leftCorners);
        right.GetWorldCorners(rightCorners);
        var leftMin = leftCorners[0];
        var leftMax = leftCorners[2];
        var rightMin = rightCorners[0];
        var rightMax = rightCorners[2];
        _ = a;
        _ = b;
        return leftMin.x < rightMax.x && leftMax.x > rightMin.x &&
            leftMin.y < rightMax.y && leftMax.y > rightMin.y;
    }

    private sealed class SnapshotSession : IRuntimeSession
    {
        private readonly RuntimeSnapshotEnvelope _snapshot;
        public bool ThrowOnGetSnapshot { get; set; }

        public SnapshotSession(RuntimeSnapshotEnvelope snapshot) { _snapshot = snapshot; }

        public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
        {
            if (ThrowOnGetSnapshot) throw new InvalidOperationException("session unavailable");
            return _snapshot;
        }

        public RuntimeActionSubmission Submit(RuntimeGameAction action)
        {
            throw new NotSupportedException();
        }

        public void Close() { }
    }
}
}
#endif
