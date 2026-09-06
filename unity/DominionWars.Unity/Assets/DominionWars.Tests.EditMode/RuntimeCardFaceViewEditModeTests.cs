#if UNITY_INCLUDE_TESTS
#nullable enable annotations

using System;
using System.Collections.Generic;
using DominionWars.Adapters;
using DominionWars.Data;
using DominionWars.Engine.Model;
using DominionWars.Unity.UI;
using NUnit.Framework;
using UnityEngine;

namespace DominionWars.Unity.EditMode
{

[TestFixture]
public sealed class RuntimeCardFaceViewEditModeTests
{
    [Test]
    public void CompactFaceKeepsReadableRulesAndUsesSnapshotStatsOnField()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceRoot", typeof(RectTransform));
            var model = Model(
                RuntimeCardZone.OwnField,
                new RuntimeCardSnapshot
                {
                    CardId = "readable_machine",
                    EntityId = 7,
                    OwnerPlayer = 0,
                    CurrentAttack = 2,
                    CurrentHealth = 3,
                });

            var face = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>(),
                "ReadableFace",
                RuntimeCardFaceMode.Compact);
            face.Bind(model);

            Assert.That(face.TitleText.text, Is.EqualTo("Readable Machine"));
            Assert.That(face.CostBadge.gameObject.activeSelf, Is.False,
                "The legacy standalone COST badge is diagnostic-only on the player face.");
            Assert.That(face.CostValue.text, Is.EqualTo("4"));
            Assert.That(face.PunishValue.text, Is.EqualTo("2"));
            Assert.That(face.RulesText.text, Does.Contain("提交后获得强化"));
            Assert.That(face.RulesText.text, Does.Contain("机械"));
            Assert.That(face.RulesText.gameObject.activeInHierarchy, Is.True);
            Assert.That(face.AttackValue.text, Is.EqualTo("2"));
            Assert.That(face.HealthValue.text, Is.EqualTo("3"));

            face.SetDiagnosticsVisible(true);
            Assert.That(face.CostBadge.gameObject.activeSelf, Is.True);
            Assert.That(face.CostLabel.text, Is.EqualTo("COST"));
            Assert.That(face.CostValue.text, Is.EqualTo("4"));
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void HandUsesPrintedStatsWhileMissingFieldRuntimeStatsStayHidden()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceStatRoot", typeof(RectTransform));
            var parent = root.GetComponent<RectTransform>();
            var card = new RuntimeCardSnapshot
            {
                CardId = "readable_machine",
                EntityId = 8,
                OwnerPlayer = 0,
            };

            var hand = RuntimeCardFaceView.Build(parent, "HandFace", RuntimeCardFaceMode.Compact);
            hand.Bind(Model(RuntimeCardZone.OwnHand, card));
            Assert.That(hand.AttackValue.text, Is.EqualTo("4"));
            Assert.That(hand.HealthValue.text, Is.EqualTo("5"));

            var field = RuntimeCardFaceView.Build(parent, "FieldFace", RuntimeCardFaceMode.Compact);
            field.Bind(Model(RuntimeCardZone.OwnField, card));
            Assert.That(field.StatsRoot.gameObject.activeSelf, Is.False);
            Assert.That(field.AttackValue.text, Is.Empty);
            Assert.That(field.HealthValue.text, Is.Empty);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InteractionEmphasisConsumesCallerStateWithoutAddingGameplayComponents()
    {
        GameObject? root = null;
        try
        {
            root = new GameObject("CardFaceInteractionRoot", typeof(RectTransform));
            var face = RuntimeCardFaceView.Build(
                root.GetComponent<RectTransform>(),
                "InteractionFace",
                RuntimeCardFaceMode.Compact);
            face.Bind(Model(
                RuntimeCardZone.OwnHand,
                new RuntimeCardSnapshot { CardId = "readable_machine", EntityId = 9, OwnerPlayer = 0 }));

            face.SetInteractionState(true, false, true);
            Assert.That(face.transform.Find("CardInteractionMarker")!.gameObject.activeSelf, Is.True);
            Assert.That(
                face.transform.Find("CardInteractionMarker/Label")!
                    .GetComponent<UnityEngine.UI.Text>().text,
                Is.EqualTo("DRAG"));
            Assert.That(face.transform.Find("DisabledVeil")!.gameObject.activeSelf, Is.False);

            face.SetInteractionState(false, true, false);
            Assert.That(
                face.transform.Find("CardInteractionMarker/Label")!
                    .GetComponent<UnityEngine.UI.Text>().text,
                Is.EqualTo("TARGET"));
            Assert.That(face.transform.Find("DisabledVeil")!.gameObject.activeSelf, Is.True);
            Assert.That(face.GetComponent<RuntimeBattleCardDrag>(), Is.Null);
            Assert.That(face.GetComponent<RuntimeBattleDropZone>(), Is.Null);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static RuntimeCardDisplayModel Model(RuntimeCardZone zone, RuntimeCardSnapshot snapshot)
    {
        var definition = new CardDefinition(
            "readable_machine",
            "Readable Machine",
            4,
            5,
            true,
            false,
            faction: "机械遗迹",
            text: "提交后获得强化。",
            cost: 4,
            punish: 2,
            keywords: new[] { "机械" },
            tags: new[] { "机械" },
            type: "MINION");
        var catalog = new CardCatalog(new Dictionary<string, CardDefinition>(StringComparer.Ordinal)
        {
            [definition.Id] = definition,
        });
        var own = new RuntimePlayerSnapshot { PlayerId = "player_0" };
        if (zone == RuntimeCardZone.OwnHand) own.Hand = new[] { snapshot };
        else own.Field = new[] { snapshot };
        var envelope = new RuntimeSnapshotEnvelope
        {
            ContractVersion = ContractVersionGuard.ExpectedVersion,
            MatchId = "card_face_test",
            SnapshotRevision = 1,
            Turn = 1,
            Phase = "ACTION",
            CurrentPlayer = 0,
            ViewerPlayerId = "player_0",
            Players = new[]
            {
                own,
                new RuntimePlayerSnapshot { PlayerId = "player_1" },
            },
            Castle = new RuntimeCastleSnapshot { Enabled = true, Health = 75 },
            LegalActions = Array.Empty<RuntimeLegalAction>(),
        };
        return RuntimeCardDisplayModel.BuildVisibleCards(envelope, catalog)[0];
    }
}
}
#endif
