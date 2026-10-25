using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

namespace SepCore.Editor
{
    [InitializeOnLoad]
    public class SceneSwitchLeft
    {
        static SceneSwitchLeft()
        {
            // 注册到全局工具栏绘制事件
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            // 在 Scene 视图的左上角绘制一个下拉菜单
            Handles.BeginGUI();

            GUILayout.BeginArea(new Rect(10, 10, 200, 100));
            if (EditorGUILayout.DropdownButton(new GUIContent("快速切换场景"), FocusType.Passive,
                    EditorStyles.toolbarDropDown))
            {
                ShowSceneMenu();
            }

            GUILayout.EndArea();

            Handles.EndGUI();
        }

        static void ShowSceneMenu()
        {
            GenericMenu menu = new GenericMenu();

            // 查找项目中所有启用（Enabled）的场景
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");

            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);

                menu.AddItem(new GUIContent(name), false, () =>
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(path);
                    }
                });
            }

            menu.ShowAsContext();
        }
    }
}
