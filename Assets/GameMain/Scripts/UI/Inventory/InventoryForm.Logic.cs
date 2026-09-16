using System;
using System.Collections.Generic;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 物品列表界面逻辑（手写 partial，与自动生成的 InventoryForm.cs 合并）。
    /// 支持俄罗斯方块式多格网格渲染、长按拖拽、右键旋转与合法性校验回弹。
    /// 注意：本表单作为嵌套表单挂在 WarehouseForm 下，UGF 生命周期不会被框架调用，
    /// 事件绑定改为幂等式（EnsureListenersBound）。
    /// </summary>
    public partial class InventoryForm : UGuiForm
    {
        private enum DragState
        {
            Idle,
            Pressed,
            Dragging,
        }

        private bool _listenersBound = false;
        private IReadOnlyList<GridItemStack> _stacks = null;
        private GridItemContainer _container = null;
        private readonly List<WarehouseSlotItem> _backgroundSlots = new List<WarehouseSlotItem>();
        private readonly Dictionary<int, WarehouseSlotItem> _itemSlots = new Dictionary<int, WarehouseSlotItem>();
        private readonly Dictionary<int, int> _instanceIdToItemId = new Dictionary<int, int>();
        private int _selectedInstanceId = 0;

        // 拖拽与长按交互状态
        private DragState _dragState = DragState.Idle;
        private WarehouseSlotItem _pressedSlot = null;
        private int _pressedInstanceId = 0;
        private float _pressTime = 0f;
        private Vector2 _pressMousePos = Vector2.zero;
        private int _dragPointerId = -1;
        private GridItemInstance _dragItem = null;
        private bool _dragCurrentRotated = false;
        private Vector2Int _dragTargetGridPos = Vector2Int.zero;
        private bool _dragTargetValid = false;

        /// <summary>
        /// 全局是否有任何网格背包正在处于拖拽流程中（供 ItemDragOverlayForm 防残留判定）。
        /// </summary>
        public static bool IsAnyDragging { get; private set; } = false;

        // 视觉辅助对象
        private RectTransform _ghostPreview = null;
        private Image _ghostPreviewImage = null;
        private WarehouseSlotItem _floatingItem = null;

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.sortButton.onClick.AddListener(OnSortButtonClick);
            GameEntry.Event.Subscribe(ItemRotateRequestedEventArgs.EventId, OnItemRotateRequested);
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            _listenersBound = false;
            View.sortButton.onClick.RemoveListener(OnSortButtonClick);
            GameEntry.Event.Unsubscribe(ItemRotateRequestedEventArgs.EventId, OnItemRotateRequested);
        }

        private void OnItemRotateRequested(object sender, GameEventArgs e)
        {
            if (this == null || _dragState != DragState.Dragging || _dragItem == null)
            {
                return;
            }

            _dragCurrentRotated = !_dragCurrentRotated;

            GlobalConfig global = GameEntry.Luban.Global?.Data;
            if (global == null)
            {
                return;
            }

            int columns = global.WarehouseFixedColumn;
            int slotSize = global.WarehouseSlotSize;
            int slotGap = global.WarehouseSlotGap;
            float totalWidth = columns * slotSize + (columns - 1) * slotGap;
            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(_dragItem.ItemId);
            if (config != null)
            {
                TryGetDragPointerStatus(out Vector2 currentPos, out _);
                UpdateDragVisuals(config, slotSize, slotGap, totalWidth, currentPos, global.ItemDragYOffset);
            }
        }

        private bool TryGetDragPointerStatus(out Vector2 screenPosition, out bool isReleased)
        {
            if (Input.touchSupported && Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.fingerId == _dragPointerId)
                    {
                        screenPosition = touch.position;
                        isReleased = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                        return true;
                    }
                }

                screenPosition = _pressMousePos;
                isReleased = true;
                return false;
            }

            screenPosition = Input.mousePosition;
            isReleased = Input.GetMouseButtonUp(0);
            return true;
        }

        private void Update()
        {
            if (_dragState == DragState.Idle)
            {
                return;
            }

            GlobalConfig global = GameEntry.Luban.Global?.Data;
            if (global == null)
            {
                Log.Error("Global config is not ready.");
                CancelDrag();
                return;
            }

            int columns = global.WarehouseFixedColumn;
            int slotSize = global.WarehouseSlotSize;
            int slotGap = global.WarehouseSlotGap;
            int totalSlotCount = global.WarehouseSlotCount;
            int rows = Mathf.CeilToInt((float)totalSlotCount / columns);
            float totalWidth = columns * slotSize + (columns - 1) * slotGap;

            // 1. 处于按压等待长按判定阶段
            if (_dragState == DragState.Pressed)
            {
                TryGetDragPointerStatus(out Vector2 currentPos, out bool isReleased);

                // 鼠标左键或按压手指提前松开 -> 视为快速单击
                if (isReleased)
                {
                    _dragState = DragState.Idle;
                    _dragPointerId = -1;
                    if (_pressedSlot != null)
                    {
                        GameEntry.Event.Fire(_pressedSlot, WarehouseSlotItemClickEventArgs.Create(_pressedInstanceId));
                    }
                    _pressedSlot = null;
                    return;
                }

                // 位移防抖：若在长按判定时间内移动超过阈值，判定为玩家意图是滑动 ScrollView 滚屏，立即取消抓起
                float holdDistThreshold = global.HoldDistanceThreshold > 0 ? global.HoldDistanceThreshold : 15f;
                if (Vector2.Distance(currentPos, _pressMousePos) > holdDistThreshold)
                {
                    _dragState = DragState.Idle;
                    _dragPointerId = -1;
                    _pressedSlot = null;
                    return;
                }

                float thresholdSeconds = global.DragHoldThresholdMs / 1000f;
                if (Time.unscaledTime - _pressTime >= thresholdSeconds)
                {
                    StartDragging(slotSize, slotGap, totalWidth, currentPos, global.ItemDragYOffset);
                }
                return;
            }

            // 2. 处于正在拖拽阶段
            if (_dragState == DragState.Dragging)
            {
                if (_dragItem == null || _container == null)
                {
                    CancelDrag();
                    return;
                }

                ItemConfig config = GameEntry.Luban.Get<ItemConfig>(_dragItem.ItemId);
                if (config == null)
                {
                    CancelDrag();
                    return;
                }

                // A. 检测鼠标右键单击 -> 旋转 90 度（桌面端快捷键）
                if (Input.GetMouseButtonDown(1))
                {
                    _dragCurrentRotated = !_dragCurrentRotated;
                }

                TryGetDragPointerStatus(out Vector2 currentPos, out bool isReleased);

                // B & C. 更新悬浮物跟随与网格投影
                UpdateDragVisuals(config, slotSize, slotGap, totalWidth, currentPos, global.ItemDragYOffset);

                // D. 检测鼠标左键或触控手指松开 -> 放置或回弹
                if (isReleased)
                {
                    FinishDragging(config, slotSize, slotGap);
                }
            }
        }

        private void StartDragging(int slotSize, int slotGap, float totalWidth, Vector2 screenPosition, int yOffset)
        {
            if (_pressedSlot == null || _container == null)
            {
                _dragState = DragState.Idle;
                _dragPointerId = -1;
                return;
            }

            _dragItem = _container.GetItemByInstanceId(_pressedInstanceId);
            if (_dragItem == null)
            {
                _dragState = DragState.Idle;
                _dragPointerId = -1;
                return;
            }

            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(_dragItem.ItemId);
            if (config == null)
            {
                _dragState = DragState.Idle;
                _dragPointerId = -1;
                return;
            }

            _dragState = DragState.Dragging;
            _dragCurrentRotated = _dragItem.IsRotated;

            // 原位置槽位变暗
            _pressedSlot.SetAlpha(0.25f);

            // 锁定 ScrollView，避免拖拽物品与视口滚动打架
            View.itemScroll.enabled = false;

            // 标记全局拖拽状态并打开顶层 Overlay（全屏四角压暗 + 双侧旋转按钮）
            IsAnyDragging = true;
            GameEntry.UI.OpenUIForm(UIFormType.ItemDragOverlayForm);

            // 确保辅助节点已就绪
            EnsureFloatingItemCreated();
            EnsureGhostPreviewCreated();

            if (_floatingItem != null)
            {
                _floatingItem.SetItem(new ItemStack(_dragItem.ItemId, _dragItem.Count));
            }

            if (_ghostPreview != null)
            {
                _ghostPreview.SetAsLastSibling();
            }

            // 关键：在激活显示之前，立即计算并同步首帧坐标与尺寸，防止残留历史拖拽位置与闪烁
            UpdateDragVisuals(config, slotSize, slotGap, totalWidth, screenPosition, yOffset);

            if (_floatingItem != null)
            {
                _floatingItem.gameObject.SetActive(true);
            }

            if (_ghostPreview != null)
            {
                _ghostPreview.gameObject.SetActive(true);
            }
        }

        private void UpdateDragVisuals(ItemConfig config, int slotSize, int slotGap, float totalWidth, Vector2 screenPosition, int yOffset)
        {
            if (config == null || _dragItem == null)
            {
                return;
            }

            int rawW = Math.Max(1, config.Width);
            int rawH = Math.Max(1, config.Height);
            int curW = _dragCurrentRotated ? rawH : rawW;
            int curH = _dragCurrentRotated ? rawW : rawH;
            Vector2 itemPixelSize = GridCoordinateHelper.GetItemPixelSize(curW, curH, slotSize, slotGap);

            // 应用垂直手指防遮挡偏移量
            Vector2 effectiveScreenPos = screenPosition + new Vector2(0f, yOffset);

            // 获取当前 UI 相机（Overlay 模式为 null，Camera 模式为对应 worldCamera）
            Canvas canvas = View.GetComponentInParent<Canvas>();
            Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            // 1. 悬浮物跟随鼠标/触控点（加上 yOffset 垂直偏移后居中对齐）
            if (_floatingItem != null)
            {
                RectTransform formRt = View.GetComponent<RectTransform>();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(formRt, effectiveScreenPos, uiCamera, out Vector2 localMouseOnForm))
                {
                    RectTransform floatRt = _floatingItem.GetComponent<RectTransform>();
                    floatRt.localPosition = localMouseOnForm;
                    floatRt.sizeDelta = itemPixelSize;
                }
            }

            // 2. 逆向解算目标网格坐标并更新投影虚影（以抬起后的悬浮物中心为落点推算）
            RectTransform rootRt = View.warehouseSlotRoot;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, effectiveScreenPos, uiCamera, out Vector2 mouseInRoot))
            {
                // rootRt 的 Pivot 为 (0.5, 1)，左边缘相对于锚点的局部 X 为 mouseInRoot.x + totalWidth * 0.5f
                float mouseXFromLeft = mouseInRoot.x + totalWidth * 0.5f;
                float mouseYFromTop = mouseInRoot.y;

                // 以抓取物中心为基准对齐
                float itemTopLeftX = mouseXFromLeft - itemPixelSize.x * 0.5f;
                float itemTopLeftY = mouseYFromTop + itemPixelSize.y * 0.5f;

                int targetGx = Mathf.RoundToInt(itemTopLeftX / (slotSize + slotGap));
                int targetGy = Mathf.RoundToInt(-itemTopLeftY / (slotSize + slotGap));

                _dragTargetGridPos = new Vector2Int(targetGx, targetGy);
                _dragTargetValid = _container != null && _container.CanPlaceAt(_dragItem.ItemId, targetGx, targetGy, _dragCurrentRotated, _dragItem.InstanceId);

                if (_ghostPreview != null && _ghostPreviewImage != null)
                {
                    _ghostPreview.sizeDelta = itemPixelSize;
                    _ghostPreview.anchoredPosition = GridCoordinateHelper.GetLocalPosition(targetGx, targetGy, slotSize, slotGap);
                    _ghostPreviewImage.color = _dragTargetValid
                        ? new Color(0.15f, 0.85f, 0.35f, 0.45f)
                        : new Color(0.95f, 0.25f, 0.25f, 0.45f);
                }
            }
        }

        private void FinishDragging(ItemConfig config, int slotSize, int slotGap)
        {
            _dragState = DragState.Idle;
            _dragPointerId = -1;
            IsAnyDragging = false;
            CloseOverlayForm();

            // 恢复 ScrollView 滚动能力
            View.itemScroll.enabled = true;

            // 重置并隐藏投影虚影
            if (_ghostPreview != null)
            {
                _ghostPreview.gameObject.SetActive(false);
                _ghostPreview.anchoredPosition = Vector2.zero;
                _ghostPreview.sizeDelta = Vector2.zero;
            }

            // 重置并隐藏悬浮物
            if (_floatingItem != null)
            {
                _floatingItem.gameObject.SetActive(false);
                _floatingItem.SetEmpty();
                RectTransform floatRt = _floatingItem.GetComponent<RectTransform>();
                floatRt.localPosition = Vector3.zero;
                floatRt.sizeDelta = Vector2.zero;
            }

            if (_pressedSlot != null)
            {
                _pressedSlot.SetAlpha(1.0f);

                if (_dragTargetValid && _dragItem != null && _container != null && config != null)
                {
                    int rawW = Math.Max(1, config.Width);
                    int rawH = Math.Max(1, config.Height);
                    int curW = _dragCurrentRotated ? rawH : rawW;
                    int curH = _dragCurrentRotated ? rawW : rawH;

                    // 放置合法：更新数据层与 UI 物理位置
                    if (_container.TryMoveItem(_dragItem.InstanceId, _dragTargetGridPos.x, _dragTargetGridPos.y, _dragCurrentRotated))
                    {
                        RectTransform slotRt = _pressedSlot.GetComponent<RectTransform>();
                        slotRt.sizeDelta = GridCoordinateHelper.GetItemPixelSize(curW, curH, slotSize, slotGap);
                        slotRt.anchoredPosition = GridCoordinateHelper.GetLocalPosition(_dragTargetGridPos.x, _dragTargetGridPos.y, slotSize, slotGap);
                        _pressedSlot.SetItem(new ItemStack(_dragItem.ItemId, _dragItem.Count));

                        // 同步至内存并标记布局为脏，不执行同步磁盘 I/O
                        SyncToMemory();

                        // 放置成功后保持选中高亮并通知外部详情面板刷新
                        SetSelectedSlot(_dragItem.InstanceId);
                        GameEntry.Event.Fire(_pressedSlot, WarehouseSlotItemClickEventArgs.Create(_dragItem.InstanceId));
                    }
                }
                // 若非法则保持原位置，透明度恢复 1.0 即可自动回弹原位
            }

            _dragTargetValid = false;
            _dragTargetGridPos = Vector2Int.zero;
            _pressedSlot = null;
            _dragItem = null;
            _pressedInstanceId = 0;
        }

        private void CancelDrag()
        {
            _dragState = DragState.Idle;
            _dragPointerId = -1;
            IsAnyDragging = false;
            CloseOverlayForm();

            // 恢复 ScrollView 滚动能力
            View.itemScroll.enabled = true;

            if (_pressedSlot != null)
            {
                _pressedSlot.SetAlpha(1.0f);
                _pressedSlot = null;
            }

            // 重置并隐藏投影虚影
            if (_ghostPreview != null)
            {
                _ghostPreview.gameObject.SetActive(false);
                _ghostPreview.anchoredPosition = Vector2.zero;
                _ghostPreview.sizeDelta = Vector2.zero;
            }

            // 重置并隐藏悬浮物
            if (_floatingItem != null)
            {
                _floatingItem.gameObject.SetActive(false);
                _floatingItem.SetEmpty();
                RectTransform floatRt = _floatingItem.GetComponent<RectTransform>();
                floatRt.localPosition = Vector3.zero;
                floatRt.sizeDelta = Vector2.zero;
            }

            _dragTargetValid = false;
            _dragTargetGridPos = Vector2Int.zero;
            _dragItem = null;
            _pressedInstanceId = 0;
        }

        private static void CloseOverlayForm()
        {
            if (GameEntry.UI.HasUIForm(UIFormType.ItemDragOverlayForm))
            {
                UGuiForm overlay = GameEntry.UI.GetUIForm(UIFormType.ItemDragOverlayForm);
                if (overlay != null)
                {
                    overlay.Close(true);
                }
            }
        }

        private void OnSlotPointerDown(WarehouseSlotItem slot, PointerEventData eventData)
        {
            if (_dragState != DragState.Idle || slot == null)
            {
                return;
            }

            _dragState = DragState.Pressed;
            _pressedSlot = slot;
            _pressedInstanceId = slot.SlotId;
            _pressTime = Time.unscaledTime;
            _pressMousePos = eventData.position;
            _dragPointerId = eventData.pointerId;
        }

        private void OnSlotPointerUp(WarehouseSlotItem slot, PointerEventData eventData)
        {
            // 在 Update 中检测 Input.GetMouseButtonUp(0) 统一处理，此处保持防抖
        }

        private void EnsureGhostPreviewCreated()
        {
            if (_ghostPreview != null)
            {
                return;
            }

            GameObject ghostGo = new GameObject("GhostPreview", typeof(RectTransform), typeof(Image));
            _ghostPreview = ghostGo.GetComponent<RectTransform>();
            _ghostPreview.SetParent(View.warehouseSlotRoot, false);
            _ghostPreview.pivot = new Vector2(0f, 1f);
            _ghostPreview.anchorMin = new Vector2(0f, 1f);
            _ghostPreview.anchorMax = new Vector2(0f, 1f);

            _ghostPreviewImage = ghostGo.GetComponent<Image>();
            _ghostPreviewImage.color = new Color(0.15f, 0.85f, 0.35f, 0.45f);
            _ghostPreviewImage.raycastTarget = false;

            ghostGo.SetActive(false);
        }

        private void EnsureFloatingItemCreated()
        {
            if (_floatingItem != null)
            {
                return;
            }

            WarehouseSlotItem template = View.warehouseSlotTemplate;
            _floatingItem = Instantiate(template, View.transform);
            _floatingItem.name = "DragFloatingItem";

            RectTransform floatRt = _floatingItem.GetComponent<RectTransform>();
            floatRt.pivot = new Vector2(0.5f, 0.5f);
            floatRt.anchorMin = new Vector2(0.5f, 0.5f);
            floatRt.anchorMax = new Vector2(0.5f, 0.5f);

            _floatingItem.EnableCustomPointerHandling(true);
            _floatingItem.SetRaycastTarget(false);
            _floatingItem.gameObject.SetActive(false);
        }

        private void OnSortButtonClick()
        {
            if (_container == null || _container.Items.Count == 0)
            {
                return;
            }

            CancelDrag();

            // 按物品价值与稀有度从大到小重新整理
            List<GridItemInstance> itemsToSort = new List<GridItemInstance>(_container.Items);
            itemsToSort.Sort((a, b) =>
            {
                ItemConfig ca = GameEntry.Luban.Get<ItemConfig>(a.ItemId);
                ItemConfig cb = GameEntry.Luban.Get<ItemConfig>(b.ItemId);
                int va = ca != null ? ca.Value : 0;
                int vb = cb != null ? cb.Value : 0;
                if (va != vb)
                {
                    return vb.CompareTo(va);
                }

                return b.ItemId.CompareTo(a.ItemId);
            });

            _container.Clear();
            for (int i = 0; i < itemsToSort.Count; i++)
            {
                _container.TryAutoInsert(itemsToSort[i].ItemId, itemsToSort[i].Count, out _);
            }

            SyncToMemory();
            RebuildGrid();
        }

        private void OnDisable()
        {
            CancelDrag();
            UnbindListeners();
            if (GameEntry.Save != null && GameEntry.Save.IsReady)
            {
                GameEntry.Save.SaveDirty();
            }
        }

        private void OnDestroy()
        {
            CancelDrag();
            UnbindListeners();
        }

        /// <summary>
        /// 设置列表数据并根据二维网格规则重建仓库。
        /// </summary>
        public void RefreshList(IReadOnlyList<GridItemStack> stacks)
        {
            EnsureListenersBound();
            CancelDrag();

            _stacks = stacks;
            RebuildGrid();
        }

        private void SyncToMemory()
        {
            if (_container == null || GameEntry.Save?.Data == null)
            {
                return;
            }

            List<GridItemStack> savedList = _container.ToSaveData();
            GameEntry.Save.Data.mainWarehouse = savedList;
            _stacks = savedList;
            GameEntry.Save.MarkWarehouseLayoutDirty();
        }

        private void RebuildGrid()
        {
            InventoryView inventoryView = View;
            GlobalConfig global = GameEntry.Luban.Global?.Data;
            if (global == null)
            {
                Log.Error("Global config is not ready.");
                return;
            }

            int totalSlotCount = global.WarehouseSlotCount;
            int columns = global.WarehouseFixedColumn;
            int slotSize = global.WarehouseSlotSize;
            int slotGap = global.WarehouseSlotGap;

            if (totalSlotCount <= 0 || columns <= 0 || slotSize <= 0)
            {
                Log.Error("Warehouse grid config is invalid: totalSlotCount={0}, columns={1}, slotSize={2}.",
                    totalSlotCount, columns, slotSize);
                return;
            }

            int rows = Mathf.CeilToInt((float)totalSlotCount / columns);

            // 1. 安全处理：禁用检查布局用的排版组件，避免覆盖代码绝对坐标计算
            GridLayoutGroup gridLayout = inventoryView.warehouseSlotRoot.GetComponent<GridLayoutGroup>();
            if (gridLayout != null && gridLayout.enabled)
            {
                gridLayout.enabled = false;
            }

            ContentSizeFitter sizeFitter = inventoryView.warehouseSlotRoot.GetComponent<ContentSizeFitter>();
            if (sizeFitter != null && sizeFitter.enabled)
            {
                sizeFitter.enabled = false;
            }

            // 2. 调整容器根节点尺寸与锚点
            inventoryView.warehouseSlotRoot.pivot = new Vector2(0.5f, 1f);
            inventoryView.warehouseSlotRoot.sizeDelta = GridCoordinateHelper.GetTotalContainerSize(columns, rows, slotSize, slotGap);

            // 3. 清理除了模板和辅助节点之外的所有旧子节点
            WarehouseSlotItem template = inventoryView.warehouseSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = inventoryView.warehouseSlotRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = inventoryView.warehouseSlotRoot.GetChild(i);
                if (child == template.transform || (_ghostPreview != null && child == _ghostPreview))
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            _backgroundSlots.Clear();
            _itemSlots.Clear();
            _instanceIdToItemId.Clear();

            // 4. 底层渲染：生成静态单格底框标尺
            for (int i = 0; i < totalSlotCount; i++)
            {
                int gx = i % columns;
                int gy = i / columns;

                WarehouseSlotItem bgSlot = Instantiate(template, inventoryView.warehouseSlotRoot);
                bgSlot.gameObject.SetActive(true);

                RectTransform rt = bgSlot.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = GridCoordinateHelper.GetLocalPosition(gx, gy, slotSize, slotGap);
                rt.sizeDelta = new Vector2(slotSize, slotSize);

                bgSlot.SetEmpty();
                _backgroundSlots.Add(bgSlot);
            }

            // 5. 顶层渲染：通过 GridItemContainer 载入持久化坐标并渲染多格物品
            _container = new GridItemContainer(columns, rows, id => GameEntry.Luban.Get<ItemConfig>(id));

            if (_stacks != null)
            {
                _container.LoadFromSaveData(_stacks);

                for (int i = 0; i < _container.Items.Count; i++)
                {
                    GridItemInstance item = _container.Items[i];
                    WarehouseSlotItem itemSlot = Instantiate(template, inventoryView.warehouseSlotRoot);
                    itemSlot.gameObject.SetActive(true);

                    RectTransform rt = itemSlot.GetComponent<RectTransform>();
                    rt.pivot = new Vector2(0f, 1f);

                    int itemW = item.GetWidth(id => GameEntry.Luban.Get<ItemConfig>(id));
                    int itemH = item.GetHeight(id => GameEntry.Luban.Get<ItemConfig>(id));

                    rt.sizeDelta = GridCoordinateHelper.GetItemPixelSize(itemW, itemH, slotSize, slotGap);
                    rt.anchoredPosition = GridCoordinateHelper.GetLocalPosition(item.AnchorX, item.AnchorY, slotSize, slotGap);

                    itemSlot.SetSlotId(item.InstanceId);
                    itemSlot.SetItem(new ItemStack(item.ItemId, item.Count));
                    itemSlot.SetSelected(item.InstanceId == _selectedInstanceId);

                    // 开启自定义指针处理并监听长按/拖拽
                    itemSlot.EnableCustomPointerHandling(true);
                    itemSlot.OnSlotPointerDown += OnSlotPointerDown;
                    itemSlot.OnSlotPointerUp += OnSlotPointerUp;

                    _itemSlots[item.InstanceId] = itemSlot;
                    _instanceIdToItemId[item.InstanceId] = item.ItemId;
                }
            }

            // 6. 更新容量统计文本
            int usedSlots = _container.UsedSlotsCount;
            inventoryView.itemCountFormatText.Set(usedSlots, totalSlotCount);
        }

        /// <summary>
        /// 按物品实例 ID 高亮对应格子，其余格子清除选中。
        /// </summary>
        public void SetSelectedSlot(int slotId)
        {
            _selectedInstanceId = slotId;
            foreach (KeyValuePair<int, WarehouseSlotItem> pair in _itemSlots)
            {
                pair.Value.SetSelected(pair.Key == slotId);
            }
        }

        /// <summary>
        /// 获取指定实例 ID 对应的物品 ID；空格或越界返回 0。
        /// </summary>
        public int GetItemIdAtSlot(int slotId)
        {
            if (_instanceIdToItemId.TryGetValue(slotId, out int itemId))
            {
                return itemId;
            }

            return 0;
        }
    }
}
