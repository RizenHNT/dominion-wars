#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
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
                Has.Some.Property("text").EqualTo("NAME  统领"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("STATUS  present"));
            Assert.That(opponentLeader!.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("opponent_leader#32"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").EqualTo("NAME  统领"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.Some.Property("text").Contain("STATUS  sealed"));
            Assert.That(ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));
            Assert.That(opponentLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                Has.None.Property("text").Contain("Unavailable"));

            RuntimeBattlePanelView.SetLeaderSlot(ownLeader, "Future Leader", "75", "present");
            var futureLeaderText = ownLeader.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(futureLeaderText, Has.Some.Property("text").EqualTo("NAME  Future Leader\nLIFE  75"));
            Assert.That(futureLeaderText, Has.Some.Property("text").EqualTo("STATUS  present"));

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
            Assert.That(ownField, Is.Not.Null);
            Assert.That(ownField!.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            Assert.That(ownField.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            var zones = ownField.GetComponents<RuntimeBattleDropZone>();
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
            var ownCards = Find(panelObject, "RuntimeBattlePanelOwn/OwnAmbushZone/OwnAmbushZoneCards");
            var opponentCards = Find(panelObject, "RuntimeBattlePanelOpponent/OpponentAmbushZone/OpponentAmbushZoneCards");
            Assert.That(ownZone, Is.Not.Null);
            Assert.That(ownZone!.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(ownZone.GetComponents<RuntimeBattleDropZone>(), Has.Length.EqualTo(1));
            Assert.That(ownZone.GetComponent<RuntimeBattleDropZone>().ActionType, Is.EqualTo("SET_AMBUSH"));
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
