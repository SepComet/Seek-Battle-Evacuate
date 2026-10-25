using SepCore.Base;
using SepCore.Definition;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 撤离点实体逻辑。
    /// 触发器型撤离：领队进入撤离点触发器后自动推进撤离进度，无需额外交互；
    /// 进度达到全局表 EvacuateTimeMs 后触发成功撤离结算。
    /// 领队离开触发器或进入战斗（探索暂停）时撤离进度归零。
    /// </summary>
    public sealed class EvacuatePointLogic : EntityBase
    {
        private EvacuatePointData _data;
        private Slider _slider;
        private float _progressSeconds;
        private float _totalSeconds;
        private bool _leaderInside;
        private bool _settlementRequested;

        /// <summary>
        /// 当前绑定的撤离点实体数据。
        /// </summary>
        public EvacuatePointData Data => _data;

        /// <summary>
        /// 撤离点当前是否开放。
        /// </summary>
        public bool IsOpen => _data != null && _data.IsOpen;

        /// <summary>
        /// 当前撤离进度（秒）。
        /// </summary>
        public float ProgressSeconds => _progressSeconds;

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _data = userData as EvacuatePointData;
            if (_data == null)
            {
                Log.Error("Evacuate point entity data is invalid.");
                return;
            }

            GlobalConfig global = GameEntry.Luban.Global != null ? GameEntry.Luban.Global.Data : null;
            if (global == null || global.EvacuateTimeMs <= 0)
            {
                Log.Error("GlobalConfig.EvacuateTimeMs is not configured.");
                _totalSeconds = 0f;
            }
            else
            {
                _totalSeconds = global.EvacuateTimeMs / 1000f;
            }

            _slider = GetComponentInChildren<Slider>(true);
            if (_slider != null)
            {
                _slider.minValue = 0f;
                _slider.maxValue = 1f;
                _slider.value = 0f;
                _slider.gameObject.SetActive(false);
            }

            _progressSeconds = 0f;
            _leaderInside = false;
            _settlementRequested = false;
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            _data = null;
            _slider = null;
            _progressSeconds = 0f;
            _totalSeconds = 0f;
            _leaderInside = false;
            _settlementRequested = false;
            base.OnHide(isShutdown, userData);
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            if (_data == null || _settlementRequested)
            {
                return;
            }

            // 进入战斗（探索暂停）时撤离进度清零
            if (GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsExplorationPaused)
            {
                if (_leaderInside && _progressSeconds > 0f)
                {
                    Log.Info("[EvacuatePoint] Battle started, evacuation progress reset to zero.");
                }

                ResetProgress();
                return;
            }

            if (!_leaderInside || _totalSeconds <= 0f)
            {
                return;
            }

            _progressSeconds += elapseSeconds;
            UpdateSlider();

            if (_progressSeconds >= _totalSeconds)
            {
                CompleteEvacuation();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsOpen || _leaderInside || _settlementRequested)
            {
                return;
            }

            if (!IsLeaderCollider(other))
            {
                return;
            }

            _leaderInside = true;
            if (_slider != null)
            {
                _slider.gameObject.SetActive(true);
            }

            Log.Info("[EvacuatePoint] Leader entered extraction zone, evacuation progress starts.");
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!_leaderInside || !IsLeaderCollider(other))
            {
                return;
            }

            _leaderInside = false;
            ResetProgress();
            Log.Info("[EvacuatePoint] Leader left extraction zone, evacuation progress reset to zero.");
        }

        private static bool IsLeaderCollider(Collider2D other)
        {
            PlayerCharacterLogic character = other.GetComponentInParent<PlayerCharacterLogic>();
            return character != null && character.IsLeader;
        }

        private void ResetProgress()
        {
            _progressSeconds = 0f;
            UpdateSlider();
            if (_slider != null)
            {
                _slider.gameObject.SetActive(false);
            }
        }

        private void UpdateSlider()
        {
            if (_slider != null)
            {
                _slider.value = _totalSeconds > 0f ? Mathf.Clamp01(_progressSeconds / _totalSeconds) : 0f;
            }
        }

        private void CompleteEvacuation()
        {
            _settlementRequested = true;
            if (_slider != null)
            {
                _slider.gameObject.SetActive(false);
            }

            Log.Info("[EvacuatePoint] Evacuation completed, requesting run settlement.");
            GameEntry.Event.Fire(this, EvacuationCompletedEventArgs.Create());
        }
    }
}