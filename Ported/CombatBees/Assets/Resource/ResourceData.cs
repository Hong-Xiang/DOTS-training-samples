using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

partial struct ResourceComponent : IComponentData
{
}

partial struct ResourceHolderEntity : IComponentData
{
    public Entity Holder;
}

partial struct ResourceHolderTeam : IComponentData
{
    public int Team;
}

partial struct ResourceHolderAspect : IComponentData
{
    RefRO<ResourceHolderEntity> holderEntity;
    RefRO<ResourceHolderTeam> holderTeam;
    public Entity Holder
    {
        get => holderEntity.ValueRO.Holder;
    }
    public int Team
    {
        get => holderTeam.ValueRO.Team;
    }
}

partial struct Stacked : IComponentData
{
    public int Index;
}

partial struct Stacking : IComponentData
{
}