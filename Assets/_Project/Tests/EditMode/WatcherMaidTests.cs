using MaidCafe.AI;
using NUnit.Framework;

namespace MaidCafe.Tests
{
    public class WatcherMaidTests
    {
        [Test]
        public void SweetsDelayTheBellPerBoxUpToTheCap()
        {
            Assert.AreEqual(0f, WatcherMaid.ExtraRingDelay(0), "no sweets: same delay as before");
            Assert.AreEqual(0f, WatcherMaid.ExtraRingDelay(-1));
            Assert.AreEqual(WatcherMaid.RingDelayPerSweet, WatcherMaid.ExtraRingDelay(1), 1e-5f);
            Assert.AreEqual(2f * WatcherMaid.RingDelayPerSweet, WatcherMaid.ExtraRingDelay(2), 1e-5f);
            Assert.AreEqual(WatcherMaid.MaxExtraRingDelay, WatcherMaid.ExtraRingDelay(3), 1e-5f);
            Assert.AreEqual(WatcherMaid.MaxExtraRingDelay, WatcherMaid.ExtraRingDelay(50), 1e-5f);
        }
    }
}
