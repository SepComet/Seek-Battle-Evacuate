using System.Collections.Generic;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 多输入源合成器。
    /// 支持同时聚合多个输入源（如旧版键鼠输入与 UI 虚拟摇杆），方便跨平台调试与混合操作。
    /// </summary>
    public sealed class CompositeCharacterInput : ICharacterInput
    {
        private readonly List<ICharacterInput> _sources = new List<ICharacterInput>();
        private bool _enabled = true;

        /// <summary>
        /// 是否启用输入。禁用时 MoveVector 归零，所有交互状态返回 false。
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public IReadOnlyList<ICharacterInput> Sources => _sources;

        public void AddSource(ICharacterInput source)
        {
            if (source != null && !_sources.Contains(source))
            {
                _sources.Add(source);
            }
        }

        public bool RemoveSource(ICharacterInput source)
        {
            return _sources.Remove(source);
        }

        public void ClearSources()
        {
            _sources.Clear();
        }

        public Vector2 MoveVector
        {
            get
            {
                if (!_enabled)
                {
                    return Vector2.zero;
                }

                Vector2 combined = Vector2.zero;
                for (int i = 0; i < _sources.Count; i++)
                {
                    ICharacterInput src = _sources[i];
                    if (src == null)
                    {
                        continue;
                    }

                    Vector2 v = src.MoveVector;
                    if (v.sqrMagnitude > 0.0001f)
                    {
                        combined += v;
                    }
                }

                return combined.sqrMagnitude > 1f ? combined.normalized : combined;
            }
        }

        public bool IsInteracting
        {
            get
            {
                if (!_enabled)
                {
                    return false;
                }

                for (int i = 0; i < _sources.Count; i++)
                {
                    if (_sources[i] != null && _sources[i].IsInteracting)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool InteractTriggered
        {
            get
            {
                if (!_enabled)
                {
                    return false;
                }

                for (int i = 0; i < _sources.Count; i++)
                {
                    if (_sources[i] != null && _sources[i].InteractTriggered)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool InteractReleased
        {
            get
            {
                if (!_enabled)
                {
                    return false;
                }

                for (int i = 0; i < _sources.Count; i++)
                {
                    if (_sources[i] != null && _sources[i].InteractReleased)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool HasInput => _enabled && (MoveVector.sqrMagnitude > 0.0001f || IsInteracting || InteractTriggered || InteractReleased);
    }
}
