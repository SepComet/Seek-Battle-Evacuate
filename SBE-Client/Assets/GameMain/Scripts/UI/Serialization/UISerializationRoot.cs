using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace SepCore.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UISerializationRoot : MonoBehaviour
    {
        [Serializable]
        public sealed class NestedFormReference
        {
            [SerializeField] private UISerializationRoot root;
            [SerializeField] private bool generateReference;
            [SerializeField] private string variableName;

            public UISerializationRoot Root => root;
            public bool GenerateReference => generateReference;
            public string VariableName => variableName;

#if UNITY_EDITOR
            internal NestedFormReference(UISerializationRoot nestedRoot, string defaultVariableName)
            {
                root = nestedRoot;
                generateReference = true;
                variableName = defaultVariableName;
            }

            internal void Configure(bool shouldGenerate, string generatedVariableName)
            {
                generateReference = shouldGenerate;
                variableName = generatedVariableName;
            }

            internal bool MigrateLegacyVariableName()
            {
                if (string.IsNullOrEmpty(variableName) ||
                    !variableName.EndsWith("View", StringComparison.Ordinal) ||
                    variableName.Length <= 4)
                {
                    return false;
                }

                variableName = variableName.Substring(0, variableName.Length - 4) + "Form";
                return true;
            }
#endif
        }

        [SerializeField, HideInInspector]
        private List<UISerializationItem> serializationItems = new List<UISerializationItem>();

        [SerializeField, HideInInspector]
        [FormerlySerializedAs("nestedViewReferences")]
        private List<NestedFormReference> nestedFormReferences = new List<NestedFormReference>();

        public IReadOnlyList<UISerializationItem> SerializationItems => serializationItems;
        public IReadOnlyList<NestedFormReference> NestedFormReferences => nestedFormReferences;

#if UNITY_EDITOR
        private bool refreshQueued;

        public bool RefreshItems()
        {
            UISerializationItem[] allItems = GetComponentsInChildren<UISerializationItem>(true);
            var ownedItems = new List<UISerializationItem>(allItems.Length);
            for (int i = 0; i < allItems.Length; i++)
            {
                UISerializationItem item = allItems[i];
                if (item == null || FindNearestRoot(item.transform) != this)
                {
                    continue;
                }

                item.RefreshComponents();
                ownedItems.Add(item);
            }

            UISerializationRoot[] allRoots = GetComponentsInChildren<UISerializationRoot>(true);
            var directNestedRoots = new List<UISerializationRoot>(allRoots.Length);
            for (int i = 0; i < allRoots.Length; i++)
            {
                UISerializationRoot candidate = allRoots[i];
                if (candidate != null && candidate != this && FindNearestParentRoot(candidate.transform) == this)
                {
                    directNestedRoots.Add(candidate);
                }
            }

            bool itemsChanged = !HasSameItems(ownedItems);
            bool nestedFormsChanged = !HasSameNestedForms(directNestedRoots);
            bool nestedNamesChanged = false;
            for (int i = 0; i < directNestedRoots.Count; i++)
            {
                NestedFormReference existing = FindNestedForm(directNestedRoots[i]);
                if (existing != null)
                {
                    nestedNamesChanged |= existing.MigrateLegacyVariableName();
                }
            }

            if (!itemsChanged && !nestedFormsChanged && !nestedNamesChanged)
            {
                return false;
            }

            if (itemsChanged)
            {
                serializationItems.Clear();
                serializationItems.AddRange(ownedItems);
            }

            if (nestedFormsChanged)
            {
                var next = new List<NestedFormReference>(directNestedRoots.Count);
                for (int i = 0; i < directNestedRoots.Count; i++)
                {
                    UISerializationRoot nestedRoot = directNestedRoots[i];
                    NestedFormReference existing = FindNestedForm(nestedRoot);
                    next.Add(existing ?? new NestedFormReference(nestedRoot, CreateDefaultNestedFormVariableName(nestedRoot)));
                }

                nestedFormReferences = next;
            }

            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        public bool SetNestedFormReference(UISerializationRoot nestedRoot, bool shouldGenerate, string variableName)
        {
            if (nestedRoot == null || FindNearestParentRoot(nestedRoot.transform) != this)
            {
                throw new ArgumentException("The nested root must be directly owned by this serialization root.", nameof(nestedRoot));
            }

            RefreshItems();
            NestedFormReference reference = FindNestedForm(nestedRoot);
            if (reference == null)
            {
                throw new InvalidOperationException("The nested root is not registered.");
            }

            string normalizedName = variableName == null ? string.Empty : variableName.Trim();
            if (reference.GenerateReference == shouldGenerate && reference.VariableName == normalizedName)
            {
                return false;
            }

            reference.Configure(shouldGenerate, normalizedName);
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        public static string CreateDefaultNestedFormVariableName(UISerializationRoot nestedRoot)
        {
            string sourceName = nestedRoot == null ? "nested" : nestedRoot.gameObject.name.Replace("(Clone)", string.Empty).Trim();
            if (sourceName.EndsWith("Form", StringComparison.Ordinal) && sourceName.Length > 4)
            {
                sourceName = sourceName.Substring(0, sourceName.Length - 4);
            }
            else if (sourceName.EndsWith("UI", StringComparison.Ordinal) && sourceName.Length > 2)
            {
                sourceName = sourceName.Substring(0, sourceName.Length - 2);
            }

            string identifier = ToPascalIdentifier(sourceName);
            if (string.IsNullOrEmpty(identifier))
            {
                identifier = "Nested";
            }

            return char.ToLowerInvariant(identifier[0]) + identifier.Substring(1) + "Form";
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
            RefreshItems();
            NotifyParentRoot();
        }

        private void OnEnable()
        {
            QueueRefresh();
            NotifyParentRoot();
        }

        private void OnValidate()
        {
            QueueRefresh();
            NotifyParentRoot();
        }

        private void OnTransformChildrenChanged()
        {
            QueueRefresh();
        }

        private void OnTransformParentChanged()
        {
            QueueRefresh();
            NotifyParentRoot();
        }

        private void RefreshDelayed()
        {
            refreshQueued = false;
            if (this == null || Application.isPlaying)
            {
                return;
            }

            RefreshItems();
        }

        private bool HasSameItems(List<UISerializationItem> found)
        {
            if (serializationItems.Count != found.Count)
            {
                return false;
            }

            for (int i = 0; i < found.Count; i++)
            {
                if (serializationItems[i] != found[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasSameNestedForms(List<UISerializationRoot> found)
        {
            if (nestedFormReferences.Count != found.Count)
            {
                return false;
            }

            for (int i = 0; i < found.Count; i++)
            {
                if (nestedFormReferences[i] == null || nestedFormReferences[i].Root != found[i])
                {
                    return false;
                }
            }

            return true;
        }

        private NestedFormReference FindNestedForm(UISerializationRoot nestedRoot)
        {
            for (int i = 0; i < nestedFormReferences.Count; i++)
            {
                NestedFormReference reference = nestedFormReferences[i];
                if (reference != null && reference.Root == nestedRoot)
                {
                    return reference;
                }
            }

            return null;
        }

        private void NotifyParentRoot()
        {
            UISerializationRoot parentRoot = FindNearestParentRoot(transform);
            if (parentRoot != null)
            {
                parentRoot.QueueRefresh();
            }
        }

        private static UISerializationRoot FindNearestRoot(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                UISerializationRoot root = current.GetComponent<UISerializationRoot>();
                if (root != null)
                {
                    return root;
                }

                current = current.parent;
            }

            return null;
        }

        private static UISerializationRoot FindNearestParentRoot(Transform target)
        {
            return target == null ? null : FindNearestRoot(target.parent);
        }

        private static string ToPascalIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            bool capitalizeNext = true;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (!char.IsLetterOrDigit(current) && current != '_')
                {
                    capitalizeNext = true;
                    continue;
                }

                if (builder.Length == 0 && char.IsDigit(current))
                {
                    builder.Append('_');
                }

                builder.Append(capitalizeNext ? char.ToUpperInvariant(current) : current);
                capitalizeNext = false;
            }

            return builder.ToString();
        }
#endif
    }
}
