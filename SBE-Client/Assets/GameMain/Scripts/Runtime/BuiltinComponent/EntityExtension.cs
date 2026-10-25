using System;
using SepCore.Definition;
using SepCore.Utility;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    public static class EntityExtension
    {
        private static int _serialId = 0;

        public static EntityBase GetGameEntity(this EntityComponent entityComponent, int entityId)
        {
            UnityGameFramework.Runtime.Entity entity = entityComponent.GetEntity(entityId);
            if (entity == null)
            {
                return null;
            }

            return (EntityBase)entity.Logic;
        }

        public static void HideEntity(this EntityComponent entityComponent, EntityBase entity)
        {
            entityComponent.HideEntity(entity.Entity);
        }

        public static void AttachEntity(this EntityComponent entityComponent, EntityBase entityBase, int ownerId,
            string parentTransformPath = null, object userData = null)
        {
            entityComponent.AttachEntity(entityBase.Entity, ownerId, parentTransformPath, userData);
        }

        public static void ShowEntity<T>(this EntityComponent entityComponent, EntityDataBase data, string group,
            int priority) where T : EntityBase
        {
            if (data == null)
            {
                Log.Warning("Data is invalid.");
                return;
            }

            EntityConfig entityConfig = GameEntry.Luban.Get<EntityConfig>(data.AssetName);
            if (entityConfig == null)
            {
                Log.Warning("Can not load entity assetName '{0}' from data table.", data.AssetName);
                return;
            }

            entityComponent.ShowEntity(data.Id, typeof(T), AssetUtility.GetEntityAsset(entityConfig.PrefabPath), group,
                priority, data);
        }

        private static void ShowEntity(this EntityComponent entityComponent, Type logicType, string entityGroup,
            int priority, EntityDataBase data)
        {
            if (data == null)
            {
                Log.Warning("Data is invalid.");
                return;
            }

            EntityConfig entityConfig = GameEntry.Luban.Get<EntityConfig>(data.AssetName);
            if (entityConfig == null)
            {
                Log.Warning("Can not load entity assetName '{0}' from data table.", data.AssetName);
                return;
            }

            entityComponent.ShowEntity(data.Id, logicType, AssetUtility.GetEntityAsset(entityConfig.PrefabPath), entityGroup,
                priority, data);
        }

        public static int SerialId(this EntityComponent entityComponent)
        {
            return ++_serialId;
        }
    }
}
