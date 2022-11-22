using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;

public static class Field
{
    public static Vector3 size = new Vector3(100f, 20f, 30f);
    public static float gravity = -20f;
}

public struct Grid : IComponentData
{
    public int3 Shape;
    public float3 Size;
    public float3 minPosition;
    public float3 NearestSnappedPos(float3 pos)
    {
        var result = IndexToPosition(ToInboundIndex(PositionToIndex(pos)));
        result.y = pos.y;
        return result;
    }

    public int3 PositionToIndex(float3 pos)
    {
        return math.int3(math.floor((pos - minPosition) / Step));
    }
    public float3 IndexToPosition(int3 idx)
    {
        return (math.float3(idx) + .5f) * Step + minPosition;

    }
    public float3 LocalToWorld(float3 local)
    {
        return local * Size + minPosition;
    }
    public int3 ToInboundIndex(int3 idx)
    {
        return math.min(math.max(idx, math.int3(0)), Shape - 1);
    }
    public float3 Step => Size / Shape;
    public float bottom => minPosition.y;
}

struct StackHeight : IBufferElementData
{
    int Value; // should never access directly, following idiom of ECS samples / GridPath
}

struct NativeArray2DProxy<T> where T : unmanaged
{
    public int2 shape;
    public NativeArray<T> data;
    public T this[int idx, int idy]
    {
        get => data[idx * shape.y + idy];
        set => data[idx * shape.y + idy] = value;
    }
}


[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
partial struct FieldDiscritizationSystem : ISystem
{
    EntityArchetype discritizationType;
    public void OnCreate(ref SystemState state)
    {
        discritizationType = state.EntityManager.CreateArchetype(typeof(Grid));

    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var discritization = state.EntityManager.CreateEntity(discritizationType);
        var heightBuffer = state.EntityManager.AddBuffer<StackHeight>(discritization);
        var grid = new Grid
        {
            Shape = math.int3(math.ceil(math.float3(Field.size) / config.resourceSize)),
            Size = Field.size,
            minPosition = -math.float3(Field.size) * .5f,
        };
        state.EntityManager.SetComponentData(discritization, grid);
        heightBuffer.Resize(grid.Shape.x * grid.Shape.z, NativeArrayOptions.ClearMemory);
        state.Enabled = false;
    }
}