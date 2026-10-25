using NUnit.Framework;
using SepCore.Net;

namespace SepCore.Tests
{
    public class NetworkLatencyMonitorTests
    {
        [Test]
        public void Samples_ExposeLatestAndWindowStatistics()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.Reset(0);
            monitor.BeginProbe(1, 0);
            Assert.That(monitor.TryComplete(1, true, 0.1), Is.True);
            monitor.BeginProbe(2, 2);
            monitor.TryComplete(2, true, 2.3);

            Assert.That(monitor.SampleCount, Is.EqualTo(2));
            Assert.That(monitor.LatestMilliseconds, Is.EqualTo(300).Within(0.001));
            Assert.That(monitor.AverageMilliseconds, Is.EqualTo(200).Within(0.001));
            Assert.That(monitor.MinimumMilliseconds, Is.EqualTo(100).Within(0.001));
            Assert.That(monitor.MaximumMilliseconds, Is.EqualTo(300).Within(0.001));
        }

        [Test]
        public void ProbeInterval_AndPendingRequestPreventOverlappingProbes()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.Reset(10);
            Assert.That(monitor.ShouldProbe(10), Is.True);
            monitor.BeginProbe(1, 10);
            Assert.That(monitor.ShouldProbe(12), Is.False);
            monitor.TryComplete(1, true, 12.1);
            Assert.That(monitor.ShouldProbe(12.1), Is.True);
            monitor.BeginProbe(2, 12.1);
            monitor.TryComplete(2, true, 12.2);
            Assert.That(monitor.ShouldProbe(14), Is.False);
            Assert.That(monitor.ShouldProbe(14.1), Is.True);
        }

        [Test]
        public void WindowExpiry_RemovesOldSamplesAndRecalculatesMean()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.BeginProbe(1, 0);
            monitor.TryComplete(1, true, 0.1);
            monitor.BeginProbe(2, 10);
            monitor.TryComplete(2, true, 10.3);
            monitor.ShouldProbe(30.2);
            Assert.That(monitor.SampleCount, Is.EqualTo(1));
            Assert.That(monitor.AverageMilliseconds, Is.EqualTo(300).Within(0.001));
            monitor.ShouldProbe(40.4);
            Assert.That(monitor.HasSamples, Is.False);
            Assert.That(monitor.LatestMilliseconds, Is.Zero);
        }

        [Test]
        public void Timeout_ClearsOldWindowAndIgnoresLateResponse()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.BeginProbe(1, 0);
            monitor.TryComplete(1, true, 0.1);
            monitor.BeginProbe(2, 2);
            Assert.That(monitor.ShouldProbe(6.99), Is.False);
            Assert.That(monitor.ShouldProbe(7), Is.True);
            Assert.That(monitor.IsTimedOut, Is.True);
            Assert.That(monitor.HasSamples, Is.False);
            monitor.BeginProbe(3, 7);
            Assert.That(monitor.TryComplete(2, true, 7.1), Is.False);
            Assert.That(monitor.HasPendingProbe, Is.True);
            monitor.TryComplete(3, true, 7.2);
            Assert.That(monitor.IsTimedOut, Is.False);
            Assert.That(monitor.LatestMilliseconds, Is.EqualTo(200).Within(0.001));
        }

        [Test]
        public void ResponseAfterDeadline_IsNotAValidSampleEvenBeforeUpdate()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.BeginProbe(1, 0);
            Assert.That(monitor.TryComplete(1, true, 5), Is.True);
            Assert.That(monitor.IsTimedOut, Is.True);
            Assert.That(monitor.HasSamples, Is.False);
        }

        [Test]
        public void Reset_DropsIdentityOfOldProbeAndOldStatistics()
        {
            NetworkLatencyMonitor monitor = new NetworkLatencyMonitor();
            monitor.BeginProbe(1, 0);
            monitor.TryComplete(1, true, 0.1);
            monitor.BeginProbe(2, 2);
            monitor.Reset(3);
            Assert.That(monitor.HasSamples, Is.False);
            Assert.That(monitor.HasPendingProbe, Is.False);
            Assert.That(monitor.TryComplete(2, true, 3.1), Is.False);
            Assert.That(monitor.ShouldProbe(3.1), Is.True);
        }
    }
}
