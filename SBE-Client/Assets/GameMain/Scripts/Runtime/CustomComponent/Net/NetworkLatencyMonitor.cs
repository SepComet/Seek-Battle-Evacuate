using System.Collections.Generic;

namespace SepCore.Net
{
    /// <summary>
    /// 应用层往返时间的滚动窗口。时钟由调用方提供，独立于 Time.timeScale。
    /// 最多保留一个探测请求；超时或重登会清掉旧样本，迟到回包不会污染新窗口。
    /// </summary>
    public sealed class NetworkLatencyMonitor
    {
        public const double ProbeIntervalSeconds = 2;
        public const double ProbeTimeoutSeconds = 5;
        public const double WindowSeconds = 30;

        private struct Sample
        {
            public double ReceivedAt;
            public double Milliseconds;
        }

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private long _pendingRid;
        private double _sentAt;
        private double _nextProbeAt;

        public bool HasSamples => _samples.Count > 0;
        public int SampleCount => _samples.Count;
        public double LatestMilliseconds { get; private set; }
        public double AverageMilliseconds { get; private set; }
        public double MinimumMilliseconds { get; private set; }
        public double MaximumMilliseconds { get; private set; }
        public bool IsTimedOut { get; private set; }
        public bool HasPendingProbe => _pendingRid != 0;

        public void Reset(double now)
        {
            ClearSamples();
            _pendingRid = 0;
            _nextProbeAt = now;
            IsTimedOut = false;
        }

        /// <summary>
        /// 每帧淘汰旧样本并处理超时，返回本帧是否应发起新探测。
        /// </summary>
        public bool ShouldProbe(double now)
        {
            while (_samples.Count > 0 && now - _samples.Peek().ReceivedAt >= WindowSeconds)
            {
                _samples.Dequeue();
            }
            Recalculate();

            if (_pendingRid != 0 && now - _sentAt >= ProbeTimeoutSeconds)
            {
                _pendingRid = 0;
                ClearSamples();
                IsTimedOut = true;
            }

            return _pendingRid == 0 && now >= _nextProbeAt;
        }

        public void BeginProbe(long rid, double now)
        {
            _pendingRid = rid;
            _sentAt = now;
            _nextProbeAt = now + ProbeIntervalSeconds;
        }

        /// <summary>
        /// 仅接受当前探测的匹配回包；失败或超过截止时间均不作为延迟样本。
        /// </summary>
        public bool TryComplete(long rid, bool accepted, double now)
        {
            if (_pendingRid == 0 || rid != _pendingRid)
            {
                return false;
            }

            _pendingRid = 0;
            if (!accepted || now - _sentAt >= ProbeTimeoutSeconds)
            {
                ClearSamples();
                IsTimedOut = true;
                return true;
            }

            _samples.Enqueue(new Sample { ReceivedAt = now, Milliseconds = (now - _sentAt) * 1000 });
            IsTimedOut = false;
            Recalculate();
            return true;
        }

        private void ClearSamples()
        {
            _samples.Clear();
            LatestMilliseconds = 0;
            AverageMilliseconds = 0;
            MinimumMilliseconds = 0;
            MaximumMilliseconds = 0;
        }

        private void Recalculate()
        {
            if (_samples.Count == 0)
            {
                ClearSamples();
                return;
            }

            double sum = 0;
            double minimum = double.MaxValue;
            double maximum = 0;
            foreach (Sample sample in _samples)
            {
                sum += sample.Milliseconds;
                if (sample.Milliseconds < minimum) minimum = sample.Milliseconds;
                if (sample.Milliseconds > maximum) maximum = sample.Milliseconds;
                LatestMilliseconds = sample.Milliseconds;
            }
            AverageMilliseconds = sum / _samples.Count;
            MinimumMilliseconds = minimum;
            MaximumMilliseconds = maximum;
        }
    }
}
