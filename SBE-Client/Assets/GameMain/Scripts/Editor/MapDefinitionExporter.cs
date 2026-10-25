#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using SepCore.Exploration;
using UnityEditor;
using UnityEngine;

namespace SepCore.Editor
{
    /// <summary>
    /// 把固定地图资产导出为服务器可加载的 JSON（DataTables/tbmapdefinition.json）。
    /// 序列化核心在运行时程序集 MapDefinitionJson.Serialize（纯函数，可被 EditMode 测试直接调用），
    /// 本菜单只是薄壳：读资产 → 校验并序列化 → 写文件。校验失败时不写文件（不留半成品）。
    /// 导出产物随 sync_config 同步到 SBE-Server/config，服务器据此重建固定布局。
    /// </summary>
    public static class MapDefinitionExporter
    {
        /// <summary>
        /// 地图资产路径：与 tbglobalconfig 的 mapDefinitionPath（"MapDefinition.asset"）指向同一资产。
        /// </summary>
        private const string MapDefinitionAssetPath = "Assets/GameMain/MapDefinition.asset";

        private const string OutputAssetPath = "Assets/GameMain/DataTables/tbmapdefinition.json";

        [MenuItem("Utility/Map/Export Map Definition JSON")]
        public static void Export()
        {
            MapDefinition mapDefinition = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapDefinitionAssetPath);
            if (mapDefinition == null)
            {
                string message = $"Map definition asset is missing at {MapDefinitionAssetPath}.";
                Debug.LogError(message);
                EditorUtility.DisplayDialog("Export Map Definition", message, "OK");
                return;
            }

            string json;
            try
            {
                json = MapDefinitionJson.Serialize(mapDefinition);
            }
            catch (InvalidOperationException exception)
            {
                // 硬性不变量被违反：报错并保持既有文件不变
                string message = $"Map definition export rejected: {exception.Message}";
                Debug.LogError(message);
                EditorUtility.DisplayDialog("Export Map Definition", message, "OK");
                return;
            }

            // 工程根目录由 dataPath 推出（CWD 可能被其他编辑器脚本改动）
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absolutePath = Path.Combine(projectRoot, OutputAssetPath);
            // UTF-8 无 BOM + LF：与 Luban 导出产物一致，避免版本库出现整文件换行差异
            File.WriteAllText(absolutePath, json, new UTF8Encoding(false));

            AssetDatabase.ImportAsset(OutputAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            Debug.Log($"Map definition exported to {OutputAssetPath}: " +
                      $"{mapDefinition.Rooms.Count} rooms, {mapDefinition.ResourcePoints.Count} resource points, " +
                      $"{mapDefinition.EnemySpawnPoints.Count} enemy spawn points, " +
                      $"{mapDefinition.PlayerSpawnPoints.Count} player spawn points, " +
                      $"{mapDefinition.ExtractionPoints.Count} extraction points.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputAssetPath);
        }
    }
}
#endif
