using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

partial struct ResourceTag : IComponentData
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

partial struct Stacked : IComponentData
{
    public int Index;
}

partial struct Stacking : IComponentData
{
}