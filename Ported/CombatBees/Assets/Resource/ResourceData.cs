using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

partial struct ResourceComponent : IComponentData
{
}

partial struct ResourceHolder : IComponentData
{
    public Entity Holder;
    public int Team;
}

partial struct Stacked : IComponentData
{
    public int Index;
}

partial struct Stacking : IComponentData
{
}