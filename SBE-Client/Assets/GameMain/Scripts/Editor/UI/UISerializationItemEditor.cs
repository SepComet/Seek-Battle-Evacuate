using System.Collections.Generic;
using SepCore.UI;
using UnityEditor;
using UnityEngine;

namespace SepCore.Editor
{
    [CustomEditor(typeof(UISerializationItem))]
    internal sealed class UISerializationItemEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var item = (UISerializationItem)target;
            item.RefreshComponents();
            serializedObject.UpdateIfRequiredOrScript();

            SerializedProperty references = serializedObject.FindProperty("componentReferences");
            EditorGUILayout.LabelField("Components", EditorStyles.boldLabel);

            var selectedNames = new HashSet<string>();
            for (int i = 0; i < references.arraySize; i++)
            {
                SerializedProperty entry = references.GetArrayElementAtIndex(i);
                SerializedProperty component = entry.FindPropertyRelative("component");
                SerializedProperty generate = entry.FindPropertyRelative("generateReference");
                SerializedProperty variableName = entry.FindPropertyRelative("variableName");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                generate.boolValue = EditorGUILayout.Toggle(generate.boolValue, GUILayout.Width(18f));

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(component.objectReferenceValue, typeof(Component), true);
                }
                EditorGUILayout.EndHorizontal();

                if (generate.boolValue)
                {
                    variableName.stringValue = EditorGUILayout.TextField("Variable Name", variableName.stringValue);
                    string normalized = variableName.stringValue == null ? string.Empty : variableName.stringValue.Trim();
                    if (!UIAssetsTools.IsValidIdentifier(normalized))
                    {
                        EditorGUILayout.HelpBox("Enter a valid C# field name.", MessageType.Error);
                    }
                    else if (!selectedNames.Add(normalized))
                    {
                        EditorGUILayout.HelpBox("This field name is already selected on this item.", MessageType.Error);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Refresh Components"))
            {
                Undo.RecordObject(item, "Refresh UI serialization components");
                item.RefreshComponents();
                serializedObject.UpdateIfRequiredOrScript();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
