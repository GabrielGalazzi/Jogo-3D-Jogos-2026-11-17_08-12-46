using MaidCafe.AI;
using MaidCafe.Core;
using NUnit.Framework;

namespace MaidCafe.Tests
{
    public class EnemyRosterTests
    {
        private const int MaxFloors = 10;

        private static readonly Difficulty[] Difficulties = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hardcore };

        [Test]
        public void NormalStartsWithTheRequestedCurve()
        {
            AssertRoster(Difficulty.Normal, 0, patrols: 1, head: false, watchers: 0); // floor 1: one maid
            AssertRoster(Difficulty.Normal, 1, patrols: 1, head: false, watchers: 1); // floor 2: one maid + bell maid
            AssertRoster(Difficulty.Normal, 2, patrols: 2, head: false, watchers: 0); // floor 3: two patrol maids
            AssertRoster(Difficulty.Normal, 3, patrols: 2, head: false, watchers: 1); // floor 4: two patrol maids + bell maid
            AssertRoster(Difficulty.Normal, 4, patrols: 2, head: true, watchers: 0);  // floor 5: the Head Maid arrives
        }

        [Test]
        public void EveryFloorAddsAtMostOneMaidAndNeverRemovesOne()
        {
            foreach (var difficulty in Difficulties)
            {
                var first = EnemyDirector.RosterFor(difficulty, 0);
                int previousTotal = first.Total;
                int previousHunters = first.Hunters;
                for (int depth = 1; depth < MaxFloors; depth++)
                {
                    var roster = EnemyDirector.RosterFor(difficulty, depth);
                    string at = $"{difficulty} floor {depth + 1} ({roster})";
                    Assert.GreaterOrEqual(roster.Total, previousTotal, at);
                    Assert.LessOrEqual(roster.Total - previousTotal, 1, at);
                    Assert.GreaterOrEqual(roster.Hunters, previousHunters, at + ": hunters never drop");
                    previousTotal = roster.Total;
                    previousHunters = roster.Hunters;
                }
            }
        }

        [Test]
        public void FirstFloorHasASingleHunterAndNoHeadMaid()
        {
            foreach (var difficulty in Difficulties)
            {
                var roster = EnemyDirector.RosterFor(difficulty, 0);
                Assert.AreEqual(1, roster.Hunters, difficulty.ToString());
                Assert.IsFalse(roster.Head, difficulty.ToString());
            }
        }

        [Test]
        public void HarderDifficultiesNeverHaveFewerMaids()
        {
            for (int depth = 0; depth < MaxFloors; depth++)
            {
                var easy = EnemyDirector.RosterFor(Difficulty.Easy, depth);
                var normal = EnemyDirector.RosterFor(Difficulty.Normal, depth);
                var hardcore = EnemyDirector.RosterFor(Difficulty.Hardcore, depth);
                Assert.LessOrEqual(easy.Total, normal.Total, $"floor {depth + 1}");
                Assert.LessOrEqual(normal.Total, hardcore.Total, $"floor {depth + 1}");
                Assert.LessOrEqual(easy.Hunters, normal.Hunters, $"floor {depth + 1}");
                Assert.LessOrEqual(normal.Hunters, hardcore.Hunters, $"floor {depth + 1}");
            }
        }

        [Test]
        public void TheHeadMaidIsTheThirdHunterAndThereIsOnlyOne()
        {
            for (int step = 1; step <= 12; step++)
            {
                var roster = EnemyDirector.RosterForStep(step);
                Assert.AreEqual(roster.Hunters >= EnemyDirector.HeadMaidFromHunters, roster.Head, $"step {step} ({roster})");
                Assert.LessOrEqual(roster.Watchers, 1, $"step {step}");
                Assert.GreaterOrEqual(roster.Patrols, 1, $"step {step}");
            }
        }

        private static void AssertRoster(Difficulty difficulty, int depth, int patrols, bool head, int watchers)
        {
            var roster = EnemyDirector.RosterFor(difficulty, depth);
            string at = $"{difficulty} floor {depth + 1}: got {roster}";
            Assert.AreEqual(patrols, roster.Patrols, at);
            Assert.AreEqual(head, roster.Head, at);
            Assert.AreEqual(watchers, roster.Watchers, at);
        }
    }
}
