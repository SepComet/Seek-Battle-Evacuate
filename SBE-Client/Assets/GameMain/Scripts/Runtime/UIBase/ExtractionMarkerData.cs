using SepCore.Entity;
using UnityEngine;

namespace SepCore.UI
{
    /// <summary>
    /// 小地图撤离点标记数据。
    /// 由主流程在打开局内 HUD 时传入，RoundHUDForm 据此把撤离点标记逐帧定位到小地图上。
    /// </summary>
    public class ExtractionMarkerData
    {
        /// <summary>
        /// 本局开放的撤离点世界坐标。
        /// </summary>
        public Vector2 ExtractionPoint { get; set; }

        /// <summary>
        /// 主摄像机实体逻辑，小地图视窗中心即其世界位置。
        /// </summary>
        public MainCameraLogic MainCamera { get; set; }

        /// <summary>
        /// 小地图相机视口覆盖的世界尺寸（宽、高）。
        /// </summary>
        public Vector2 ViewportSize { get; set; }
    }
}
