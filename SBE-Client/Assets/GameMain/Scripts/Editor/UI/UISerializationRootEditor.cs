using System.Collections.Generic;
using SepCore.UI;
using UnityEditor;
using UnityEngine;

namespace SepCore.Editor
{
    [CustomEditor(typeof(UISerializationRoot))]
    internal sealed class UISerializationRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var root = (UISerializationRoot)target;
            root.RefreshItems();
            serializedObject.UpdateIfRequiredOrScript();

            int referenceCount = 0;
            bool hasValidationError = false;
            var selectedNames = new HashSet<string>();
            for (int i = 0; i < root.SerializationItems.Count; i++)
            {
                UISerializationItem item = root.SerializationItems[i];
                if (item == null)
                {
                    continue;
                }

                for (int j = 0; j < item.ComponentReferences.Count; j++)
                {
                    UISerializationItem.ComponentReference reference = item.ComponentReferences[j];
                    if (reference.GenerateReference)
                    {
                        referenceCount++;
                        string variableName = reference.VariableName == null ? string.Empty : reference.VariableName.Trim();
                        if (!UIAssetsTools.IsValidIdentifier(variableName) || !selectedNames.Add(variableName))
                        {
                            hasValidationError = true;
                        }
                    }
                }
            }

            SerializedProperty nestedForms = serializedObject.FindProperty("nestedFormReferences");
            for (int i = 0; i < nestedForms.arraySize; i++)
            {
                SerializedProperty entry = nestedForms.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("generateReference").boolValue)
                {
                    referenceCount++;
                }
            }

            EditorGUILayout.LabelField("View Class", UIAssetsTools.GetViewClassName(root));
            EditorGUILayout.LabelField("Form Class", UIAssetsTools.GetFormClassName(root));
            EditorGUILayout.LabelField("Owned Items", root.SerializationItems.Count.ToString());
            EditorGUILayout.LabelField("Nested Forms", nestedForms.arraySize.ToString());
            EditorGUILayout.LabelField("Generated References", referenceCount.ToString());
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Owned Items", EditorStyles.boldLabel);
            for (int i = 0; i < root.SerializationItems.Count; i++)
            {
                UISerializationItem item = root.SerializationItems[i];
                if (item == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(GetRelativePath(root.transform, item.transform));
                if (GUILayout.Button("Select", GUILayout.Width(56f)))
                {
                    Selection.activeGameObject = item.gameObject;
                    EditorGUIUtility.PingObject(item.gameObject);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Nested Forms", EditorStyles.boldLabel);
            if (nestedForms.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Nested UISerializationRoot components will appear here as child Form references and form serialization boundaries.", MessageType.Info);
            }

            for (int i = 0; i < nestedForms.arraySize; i++)
            {
                SerializedProperty entry = nestedForms.GetArrayElementAtIndex(i);
                SerializedProperty nestedRootProperty = entry.FindPropertyRelative("root");
                SerializedProperty generate = entry.FindPropertyRelative("generateReference");
                SerializedProperty variableName = entry.FindPropertyRelative("variableName");
                var nestedRoot = nestedRootProperty.objectReferenceValue as UISerializationRoot;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                generate.boolValue = EditorGUILayout.Toggle(generate.boolValue, GUILayout.Width(18f));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(nestedRoot, typeof(UISerializationRoot), true);
                }
                if (nestedRoot != null && GUILayout.Button("Select", GUILayout.Width(56f)))
                {
                    Selection.activeGameObject = nestedRoot.gameObject;
                    EditorGUIUtility.PingObject(nestedRoot.gameObject);
                }
                EditorGUILayout.EndHorizontal();

                if (nestedRoot != null)
                {
                    EditorGUILayout.LabelField("Form Class", UIAssetsTools.GetFormClassName(nestedRoot));
                }

                if (generate.boolValue)
                {
                    variableName.stringValue = EditorGUILayout.TextField("Variable Name", variableName.stringValue);
                    string normalized = variableName.stringValue == null ? string.Empty : variableName.stringValue.Trim();
                    if (!UIAssetsTools.IsValidIdentifier(normalized))
                    {
                        hasValidationError = true;
                        EditorGUILayout.HelpBox("Enter a valid C# field name.", MessageType.Error);
                    }
                    else if (!selectedNames.Add(normalized))
                    {
                        hasValidationError = true;
                        EditorGUILayout.HelpBox("This field name conflicts with another generated reference.", MessageType.Error);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (hasValidationError)
            {
                EditorGUILayout.HelpBox("Fix invalid or duplicate generated field names before creating View scripts.", MessageType.Error);
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Items"))
            {
                Undo.RecordObject(root, "Refresh UI serialization items");
                root.RefreshItems();
            }
            using (new EditorGUI.DisabledScope(hasValidationError))
            {
                if (GUILayout.Button("Create View & Form Scripts"))
                {
                    UIAssetsTools.GenerateWithDialog(root);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return root.name;
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }

    [InitializeOnLoad]
    internal static class UISerializationRootWatcher
    {
        private static bool refreshQueued;

        static UISerializationRootWatcher()
        {
            EditorApplication.hierarchyChanged += QueueRefresh;
        }

        private static void QueueRefresh()
        {
            if (refreshQueued || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            refreshQueued = true;
            EditorApplication.delayCall += RefreshLoadedRoots;
        }

        private static void RefreshLoadedRoots()
        {
            refreshQueued = false;
            UISerializationRoot[] roots = Resources.FindObjectsOfTypeAll<UISerializationRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                UISerializationRoot root = roots[i];
                if (root == null || EditorUtility.IsPersistent(root) || !root.gameObject.scene.IsValid() || !root.gameObject.scene.isLoaded)
                {
                    continue;
                }

                root.RefreshItems();
            }
        }
    }
}
