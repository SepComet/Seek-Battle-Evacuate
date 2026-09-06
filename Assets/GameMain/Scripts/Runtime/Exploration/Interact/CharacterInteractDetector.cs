using System.Collections.Generic;
using SepCore.Base;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 领队可交互实体探测器。
    /// 挂载于 CharacterLeader 的 InteractTrigger 触发器子物体上，
    /// 负责捕获进入与离开触发器的场景实体、维护候选集合、实时选出最高优先级交互目标，并驱动 UI 联动与交互执行。
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterInteractDetector : MonoBehaviour
    {
        private readonly List<IInteractable> _candidates = new List<IInteractable>();
        private IInteractable _currentTarget = null;
        private PlayerCharacterController _leaderController = null;
        private IHoldInteractable _currentHoldingTarget = null;

        /// <summary>
        /// 当前最高优先级的可交互目标（无目标时为 null）。
        /// </summary>
        public IInteractable CurrentTarget => _currentTarget;

        /// <summary>
        /// 当前是否处于可交互状态。
        /// </summary>
        public bool HasInteractableTarget => _currentTarget != null && _currentTarget.CanInteract;

        /// <summary>
        /// 当前处于触发器内的全部候选实体（只读视图）。
        /// </summary>
        public IReadOnlyList<IInteractable> Candidates => _candidates;

        /// <summary>
        /// 初始化探测器，绑定领队移动与交互控制器。
        /// </summary>
        /// <param name="leaderController">领队控制器实例。</param>
        public void Initialize(PlayerCharacterController leaderController)
        {
            if (_leaderController != null)
            {
                UnbindControllerEvents();
            }

            _leaderController = leaderController;
            if (_leaderController != null)
            {
                _leaderController.OnInteractTriggered += HandleInteractTriggered;
                _leaderController.OnInteractHeld += HandleInteractHeld;
                _leaderController.OnInteractReleased += HandleInteractReleased;
            }
        }

        private void Update()
        {
            if (_currentHoldingTarget != null)
            {
                bool battlePaused = GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsExplorationPaused;
                bool inputStopped = _leaderController != null && _leaderController.InputSource != null && !_leaderController.InputSource.IsInteracting;
                if (battlePaused || inputStopped)
                {
                    CancelCurrentHold();
                }
            }

            CleanDeadCandidates();
            EvaluateCurrentTarget(forceNotify: false);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            IInteractable interactable = other.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                interactable = other.GetComponent<IInteractable>();
            }

            if (interactable == null)
            {
                return;
            }

            if (!_candidates.Contains(interactable))
            {
                _candidates.Add(interactable);
                EvaluateCurrentTarget(forceNotify: false);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            IInteractable interactable = other.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                interactable = other.GetComponent<IInteractable>();
            }

            if (interactable == null)
            {
                return;
            }

            if (_candidates.Remove(interactable))
            {
                EvaluateCurrentTarget(forceNotify: false);
            }
        }

        private void OnDisable()
        {
            CancelCurrentHold();
            _candidates.Clear();
            if (_currentTarget != null)
            {
                if (_currentTarget.EntityGameObject != null)
                {
                    _currentTarget.SetHighlight(false);
                }

                _currentTarget = null;
                NotifyTargetChanged();
            }
        }

        private void OnDestroy()
        {
            UnbindControllerEvents();
            CancelCurrentHold();
            _candidates.Clear();
            if (_currentTarget != null)
            {
                if (_currentTarget.EntityGameObject != null)
                {
                    _currentTarget.SetHighlight(false);
                }

                _currentTarget = null;
            }
        }

        private void UnbindControllerEvents()
        {
            if (_leaderController != null)
            {
                _leaderController.OnInteractTriggered -= HandleInteractTriggered;
                _leaderController.OnInteractHeld -= HandleInteractHeld;
                _leaderController.OnInteractReleased -= HandleInteractReleased;
                CancelCurrentHold();
                _leaderController = null;
            }
        }

        private void CleanDeadCandidates()
        {
            for (int i = _candidates.Count - 1; i >= 0; i--)
            {
                IInteractable candidate = _candidates[i];
                if (candidate == null || candidate.EntityGameObject == null || !candidate.EntityGameObject.activeInHierarchy)
                {
                    _candidates.RemoveAt(i);
                }
            }
        }

        private void EvaluateCurrentTarget(bool forceNotify)
        {
            Vector3 leaderPos = _leaderController != null ? _leaderController.transform.position : transform.position;
            IInteractable bestTarget = InteractTargetSelector.SelectBestTarget(_candidates, leaderPos);

            if (!ReferenceEquals(_currentTarget, bestTarget) || forceNotify)
            {
                if (_currentHoldingTarget != null && !ReferenceEquals(_currentHoldingTarget, bestTarget))
                {
                    CancelCurrentHold();
                }

                if (_currentTarget != null && _currentTarget.EntityGameObject != null)
                {
                    _currentTarget.SetHighlight(false);
                }

                _currentTarget = bestTarget;

                if (_currentTarget != null && _currentTarget.EntityGameObject != null)
                {
                    _currentTarget.SetHighlight(true);
                }

                NotifyTargetChanged();
            }
        }

        private void NotifyTargetChanged()
        {
            GameEntry.Event.Fire(this, LeaderInteractTargetChangedEventArgs.Create(_currentTarget));
        }

        private void HandleInteractTriggered(PlayerCharacterController controller)
        {
            if (_currentTarget == null || !_currentTarget.CanInteract)
            {
                return;
            }

            GameObject interactor = _leaderController != null ? _leaderController.gameObject : gameObject;
            if (_currentTarget is IHoldInteractable holdInteractable)
            {
                _currentHoldingTarget = holdInteractable;
                _currentHoldingTarget.OnInteractStart(interactor);
            }
            else
            {
                _currentTarget.OnInteract(interactor);
                // 交互后（例如道具被拾取或状态改变）重新评估当前目标
                EvaluateCurrentTarget(forceNotify: false);
            }
        }

        private void HandleInteractHeld(PlayerCharacterController controller)
        {
            if (_currentHoldingTarget == null)
            {
                return;
            }

            if (!_currentHoldingTarget.CanInteract || _currentHoldingTarget.EntityGameObject == null || !_currentHoldingTarget.EntityGameObject.activeInHierarchy)
            {
                CancelCurrentHold();
                EvaluateCurrentTarget(forceNotify: false);
                return;
            }

            GameObject interactor = _leaderController != null ? _leaderController.gameObject : gameObject;
            _currentHoldingTarget.OnInteractHold(interactor, Time.deltaTime);

            if (!_currentHoldingTarget.CanInteract)
            {
                CancelCurrentHold();
                EvaluateCurrentTarget(forceNotify: false);
            }
        }

        private void HandleInteractReleased(PlayerCharacterController controller)
        {
            if (_currentHoldingTarget != null)
            {
                CancelCurrentHold();
                EvaluateCurrentTarget(forceNotify: false);
            }
        }

        private void CancelCurrentHold()
        {
            if (_currentHoldingTarget != null)
            {
                IHoldInteractable holdTarget = _currentHoldingTarget;
                _currentHoldingTarget = null;
                GameObject interactor = _leaderController != null ? _leaderController.gameObject : gameObject;
                holdTarget.OnInteractEnd(interactor);
            }
        }
    }
}
