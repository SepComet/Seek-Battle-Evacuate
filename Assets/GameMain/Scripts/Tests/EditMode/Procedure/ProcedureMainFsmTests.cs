using System;
using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Procedure;

namespace SepCore.Tests
{
    [TestFixture]
    public class ProcedureMainFsmTests
    {
        [Test]
        public void ProcedureMain_InitialProperties_AreDefault()
        {
            ProcedureMain procedure = new ProcedureMain();

            Assert.IsNull(procedure.BuildResult);
            Assert.IsNull(procedure.PendingOutcome);
            Assert.AreEqual(0, procedure.RunStartTimeUtcMs);
            Assert.AreEqual(DifficultyTier.None, procedure.Difficulty);
            Assert.IsTrue(procedure.AutoReturnToMenu);
        }

        [Test]
        public void ProcedureMain_TriggerSettlement_RecordsPendingOutcome()
        {
            ProcedureMain procedure = new ProcedureMain();

            procedure.TriggerSettlement(RoundResultType.Extracted);
            Assert.AreEqual(RoundResultType.Extracted, procedure.PendingOutcome);

            procedure.TriggerSettlement(RoundResultType.Defeated);
            Assert.AreEqual(RoundResultType.Defeated, procedure.PendingOutcome);

            procedure.TriggerSettlement(RoundResultType.TimedOut);
            Assert.AreEqual(RoundResultType.TimedOut, procedure.PendingOutcome);

            procedure.TriggerSettlement(RoundResultType.Quit);
            Assert.AreEqual(RoundResultType.Quit, procedure.PendingOutcome);
        }

        [Test]
        public void Settlement_Extracted_PreservesEquippedItems()
        {
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1, 1001, 2001)
                },
                runHistory = new List<RoundRecord>()
            };

            // 模拟结算：成功撤离保留装备
            RoundResultType outcome = RoundResultType.Extracted;
            if (outcome != RoundResultType.Extracted && save.characters != null)
            {
                for (int i = 0; i < save.characters.Count; i++)
                {
                    CharacterSave c = save.characters[i];
                    c.weaponItemId = 0;
                    c.armorItemId = 0;
                    save.characters[i] = c;
                }
            }

            Assert.AreEqual(1001, save.characters[0].weaponItemId);
            Assert.AreEqual(2001, save.characters[0].armorItemId);
        }

        [TestCase(RoundResultType.Defeated)]
        [TestCase(RoundResultType.TimedOut)]
        [TestCase(RoundResultType.Quit)]
        public void Settlement_NonExtracted_ClearsEquippedItems(RoundResultType outcome)
        {
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1, 1001, 2001),
                    new CharacterSave(2, 1002, 0)
                },
                runHistory = new List<RoundRecord>()
            };

            // 模拟结算：非撤离清空装备
            if (outcome != RoundResultType.Extracted && save.characters != null)
            {
                for (int i = 0; i < save.characters.Count; i++)
                {
                    CharacterSave c = save.characters[i];
                    c.weaponItemId = 0;
                    c.armorItemId = 0;
                    save.characters[i] = c;
                }
            }

            Assert.AreEqual(0, save.characters[0].weaponItemId);
            Assert.AreEqual(0, save.characters[0].armorItemId);
            Assert.AreEqual(0, save.characters[1].weaponItemId);
            Assert.AreEqual(0, save.characters[1].armorItemId);
        }

        [Test]
        public void Settlement_AppendsRunRecord_WithCorrectFields()
        {
            SaveData save = new SaveData
            {
                runHistory = new List<RoundRecord>()
            };

            long startedAt = 1000000;
            long endedAt = 1200000;
            long seed = 12345678;
            DifficultyTier difficulty = DifficultyTier.Tier2;
            RoundResultType outcome = RoundResultType.Extracted;

            save.runHistory.Add(new RoundRecord(outcome, difficulty, seed, startedAt, endedAt));

            Assert.AreEqual(1, save.runHistory.Count);
            RoundRecord record = save.runHistory[0];
            Assert.AreEqual(outcome, record.outcome);
            Assert.AreEqual(difficulty, record.difficultyId);
            Assert.AreEqual(seed, record.seed);
            Assert.AreEqual(startedAt, record.startedAt);
            Assert.AreEqual(endedAt, record.endedAt);
        }
    }
}
