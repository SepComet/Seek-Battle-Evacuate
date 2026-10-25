using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SepCore.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UISerializationItem : MonoBehaviour
    {
        [Serializable]
        public sealed class ComponentReference
        {
            [SerializeField] private Component component;
            [SerializeField] private bool generateReference;
            [SerializeField] private string variableName;

            public Component Component => component;
            public bool GenerateReference => generateReference;
            public string VariableName => variableName;

#if UNITY_EDITOR
            internal ComponentReference(Component target, string defaultVariableName)
            {
                component = target;
                generateReference = false;
                variableName = defaultVariableName;
            }

            internal void Configure(bool shouldGenerate, string generatedVariableName)
            {
                generateReference = shouldGenerate;
                variableName = generatedVariableName;
            }
#endif
        }

        [SerializeField, HideInInspector]
        private List<ComponentReference> componentReferences = new List<ComponentReference>();

        public IReadOnlyList<ComponentReference> ComponentReferences => componentReferences;

#if UNITY_EDITOR
        private bool refreshQueued;

        public bool RefreshComponents()
        {
            Component[] attachedComponents = GetComponents<Component>();
            var next = new List<ComponentReference>(attachedComponents.Length);

            for (int i = 0; i < attachedComponents.Length; i++)
            {
                Component attached = attachedComponents[i];
                if (attached == null || attached == this || attached is UISerializationRoot)
                {
                    continue;
                }

                ComponentReference existing = FindReference(attached);
                next.Add(existing ?? new ComponentReference(attached, CreateDefaultVariableName(gameObject, attached)));
            }

            if (HasSameComponents(next))
            {
                return false;
            }

            componentReferences = next;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        public bool SetReference(Component component, bool shouldGenerate, string variableName)
        {
            if (component == null || component.gameObject != gameObject)
            {
                throw new ArgumentException("The component must be attached to the same GameObject as the serialization item.", nameof(component));
            }

            RefreshComponents();
            ComponentReference reference = FindReference(component);
            if (reference == null)
            {
                throw new InvalidOperationException("The component is not available for UI serialization.");
            }

            string normalizedName = variableName == null ? string.Empty : variableName.Trim();
            if (reference.GenerateReference == shouldGenerate && reference.VariableName == normalizedName)
            {
                return false;
            }

            reference.Configure(shouldGenerate, normalizedName);
            UnityEditor.EditorUtility.SetDirty(this);
            NotifyRoot();
            return true;
        }

        public static string CreateDefaultVariableName(GameObject owner, Component component)
        {
            string ownerName = ToCamelIdentifier(owner == null ? "uiItem" : owner.name);
            string typeName = component == null ? "Component" : component.GetType().Name;

            if (component is RectTransform)
            {
                typeName = "RectTransform";
            }

            if (ownerName.EndsWith(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return ownerName;
            }

            return ownerName + typeName;
        }

        internal void QueueRefresh()
        {
            if (refreshQueued || Application.isPlaying)
            {
                return;
            }

            refreshQueued = true;
            UnityEditor.EditorApplication.delayCall += RefreshDelayed;
        }

        private void Reset()
        {
            RefreshComponents();
            NotifyRoot();
        }

        private void OnEnable()
        {
            QueueRefresh();
        }

        private void OnValidate()
        {
            QueueRefresh();
        }

        private void RefreshDelayed()
        {
            refreshQueued = false;
            if (this == null || Application.isPlaying)
            {
                return;
            }

            RefreshComponents();
            NotifyRoot();
        }

        private void NotifyRoot()
        {
            UISerializationRoot root = GetComponentInParent<UISerializationRoot>(true);
            if (root != null)
            {
                root.QueueRefresh();
            }
        }

        private ComponentReference FindReference(Component component)
        {
            for (int i = 0; i < componentReferences.Count; i++)
            {
                ComponentReference reference = componentReferences[i];
                if (reference != null && reference.Component == component)
                {
                    return reference;
                }
            }

            return null;
        }

        private bool HasSameComponents(List<ComponentReference> next)
        {
            if (componentReferences.Count != next.Count)
            {
                return false;
            }

            for (int i = 0; i < next.Count; i++)
            {
                if (componentReferences[i] == null || componentReferences[i].Component != next[i].Component)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToCamelIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            bool capitalizeNext = false;

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (!char.IsLetterOrDigit(current) && current != '_')
                {
                    capitalizeNext = builder.Length > 0;
                    continue;
                }

                if (builder.Length == 0)
                {
                    if (char.IsDigit(current))
                    {
                        builder.Append('_');
                    }
                    builder.Append(char.ToLowerInvariant(current));
                    continue;
                }

                builder.Append(capitalizeNext ? char.ToUpperInvariant(current) : current);
                capitalizeNext = false;
            }

            return builder.Length == 0 ? "uiItem" : builder.ToString();
        }
#endif
    }
}
