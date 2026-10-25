using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    /// <summary>
    /// 可交互目标优先级选择算法（InteractTargetSelector）单元测试。
    /// 验证物资点优先、多道具稀有度优先、同稀有度距离优先及不可交互过滤等核心规则。
    /// </summary>
    [TestFixture]
    public class InteractTargetSelectorTests
    {
        private class MockInteractable : IInteractable
        {
            public InteractableType InteractableType { get; set; }
            public Rarity Rarity { get; set; } = Rarity.None;
            public Vector3 Position { get; set; }
            public bool CanInteract { get; set; } = true;
            public GameObject EntityGameObject { get; set; } = null;
            public bool IsHighlighted { get; private set; } = false;

            public void OnInteract(GameObject interactor)
            {
            }

            public void SetHighlight(bool highlight)
            {
                IsHighlighted = highlight;
            }
        }

        [Test]
        public void SelectBestTarget_NullOrEmptyCandidates_ReturnsNull()
        {
            Vector3 leaderPos = Vector3.zero;
            Assert.IsNull(InteractTargetSelector.SelectBestTarget(null, leaderPos));
            Assert.IsNull(InteractTargetSelector.SelectBestTarget(new List<IInteractable>(), leaderPos));
        }

        [Test]
        public void SelectBestTarget_OnlyResourcePoint_ReturnsResourcePoint()
        {
            Vector3 leaderPos = Vector3.zero;
            var res = new MockInteractable
            {
                InteractableType = InteractableType.ResourcePoint,
                Position = new Vector3(2f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { res };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            Assert.AreSame(res, best);
        }

        [Test]
        public void SelectBestTarget_OnlySingleItem_ReturnsItem()
        {
            Vector3 leaderPos = Vector3.zero;
            var item = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.White,
                Position = new Vector3(1f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { item };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            Assert.AreSame(item, best);
        }

        [Test]
        public void SelectBestTarget_ResourcePointAndHigherRarityItem_PrefersResourcePoint()
        {
            Vector3 leaderPos = Vector3.zero;

            // 道具距离更近 (1米)，且稀有度极高 (Red)
            var redItem = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Red,
                Position = new Vector3(1f, 0f, 0f)
            };

            // 物资点距离稍远 (3米)
            var resourcePoint = new MockInteractable
            {
                InteractableType = InteractableType.ResourcePoint,
                Position = new Vector3(3f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { redItem, resourcePoint };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            // 规则：最优先的是物资点
            Assert.AreSame(resourcePoint, best);
        }

        [Test]
        public void SelectBestTarget_MultipleItems_PrefersHigherRarityRegardlessOfDistance()
        {
            Vector3 leaderPos = Vector3.zero;

            // 白色物品距离很近 (0.5米)
            var whiteItem = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.White,
                Position = new Vector3(0.5f, 0f, 0f)
            };

            // 绿色物品距离较近 (1米)
            var greenItem = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Green,
                Position = new Vector3(1f, 0f, 0f)
            };

            // 金色物品距离稍远 (2米)
            var goldItem = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Gold,
                Position = new Vector3(2f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { whiteItem, greenItem, goldItem };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            // 规则：先按稀有度排序，高稀有度优先
            Assert.AreSame(goldItem, best);
        }

        [Test]
        public void SelectBestTarget_MultipleItemsSameRarity_PrefersCloserDistance()
        {
            Vector3 leaderPos = Vector3.zero;

            // 两个都是蓝色物品
            var farBlue = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Blue,
                Position = new Vector3(3f, 0f, 0f)
            };

            var nearBlue = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Blue,
                Position = new Vector3(1.5f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { farBlue, nearBlue };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            // 规则：同稀有度按距离排序，近者优先
            Assert.AreSame(nearBlue, best);
        }

        [Test]
        public void SelectBestTarget_MultipleResourcePoints_PrefersCloserDistance()
        {
            Vector3 leaderPos = Vector3.zero;

            var farRes = new MockInteractable
            {
                InteractableType = InteractableType.ResourcePoint,
                Position = new Vector3(5f, 0f, 0f)
            };

            var nearRes = new MockInteractable
            {
                InteractableType = InteractableType.ResourcePoint,
                Position = new Vector3(2f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { farRes, nearRes };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            Assert.AreSame(nearRes, best);
        }

        [Test]
        public void SelectBestTarget_CandidateCannotInteract_Ignored()
        {
            Vector3 leaderPos = Vector3.zero;

            // 物资点已不可交互（例如已搜空）
            var disabledRes = new MockInteractable
            {
                InteractableType = InteractableType.ResourcePoint,
                Position = new Vector3(1f, 0f, 0f),
                CanInteract = false
            };

            // 距离稍远的道具
            var item = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.White,
                Position = new Vector3(2f, 0f, 0f),
                CanInteract = true
            };

            var candidates = new List<IInteractable> { disabledRes, item };
            var best = InteractTargetSelector.SelectBestTarget(candidates, leaderPos);

            // 禁用的物资点被跳过，选中合法道具
            Assert.AreSame(item, best);
        }

        [Test]
        public void SelectBestTarget_MovingLeader_ChangesCloserTarget()
        {
            // 两个同为绿色的道具
            var itemA = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Green,
                Position = new Vector3(0f, 0f, 0f)
            };

            var itemB = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Green,
                Position = new Vector3(10f, 0f, 0f)
            };

            var candidates = new List<IInteractable> { itemA, itemB };

            // 领队靠近 A
            Vector3 posNearA = new Vector3(1f, 0f, 0f);
            Assert.AreSame(itemA, InteractTargetSelector.SelectBestTarget(candidates, posNearA));

            // 领队移动靠近 B
            Vector3 posNearB = new Vector3(9f, 0f, 0f);
            Assert.AreSame(itemB, InteractTargetSelector.SelectBestTarget(candidates, posNearB));
        }

        [Test]
        public void MockInteractable_SetHighlight_TogglesCorrectly()
        {
            var item = new MockInteractable
            {
                InteractableType = InteractableType.Item,
                Rarity = Rarity.Blue
            };

            Assert.IsFalse(item.IsHighlighted);
            item.SetHighlight(true);
            Assert.IsTrue(item.IsHighlighted);
            item.SetHighlight(false);
            Assert.IsFalse(item.IsHighlighted);
        }
    }
}
