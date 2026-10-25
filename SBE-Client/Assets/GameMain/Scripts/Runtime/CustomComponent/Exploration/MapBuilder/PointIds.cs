namespace SepCore.Exploration
{
    /// <summary>
    /// 交互主键构造（exploration-online-v1 契约 §6）。
    /// 物资点为 "r&lt;i&gt;"、敌点为 "e&lt;i&gt;"，i 是点位在地图定义各列表中的下标（0 起）。
    /// 生成器预测摆位与流程实例化实体都从这里取主键，双端（Go mapgen.ResourcePointID / EnemyPointID）
    /// 与服务端 run 消息的点位校验共用同一规则。
    /// </summary>
    public static class PointIds
    {
        /// <summary>物资点交互主键 "r&lt;index&gt;"。</summary>
        public static string ResourcePoint(int index)
        {
            return "r" + index;
        }

        /// <summary>敌点交互主键 "e&lt;index&gt;"。</summary>
        public static string EnemyPoint(int index)
        {
            return "e" + index;
        }
    }
}
