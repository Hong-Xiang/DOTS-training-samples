using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;


struct BeeTag : IComponentData
{
}

struct BeeSize : IComponentData
{
    public float size;
}


struct Team : ISharedComponentData
{
    public int Value;
}

struct Velocity : IComponentData
{
    public float3 Value;
}

// alive bee有四种互斥的状态
// - idle
// - EnemyTarget
// - ResourceTarget
// - HoldingResource



struct BeeRandom : IComponentData
{
    public Unity.Mathematics.Random random;
}

struct Dying : IComponentData
{
    public float Timer;
}

struct EnemyTargetEntity : IComponentData
{
    public Entity BeeEntity;
}

struct EnemyTargetVelocity : IComponentData
{
    public float3 Velocity;
}


readonly partial struct EnemyTargetAspect : IAspect
{
    public readonly Entity Self;
    readonly RefRO<EnemyTargetEntity> Target;
    readonly RefRW<EnemyTargetVelocity> EnemyVelociy;
    public Entity BeeEntity
    {
        get => Target.ValueRO.BeeEntity;
    }
    public float3 Velocity
    {
        get => EnemyVelociy.ValueRO.Velocity;
        set => EnemyVelociy.ValueRW.Velocity = value;
    }

    [BurstCompile]
    public static void AddEnemyTarget(ref EntityCommandBuffer.ParallelWriter ecb,
                                      int sortKey,
                                      in Entity self, in Entity target)
    {
        ecb.AddComponent(sortKey, self, new EnemyTargetEntity { BeeEntity = target });
        ecb.AddComponent(sortKey, self, new EnemyTargetVelocity { Velocity = float3.zero });
    }

    [BurstCompile]
    public void RemoveTarget(ref EntityCommandBuffer.ParallelWriter ecb,
                             int sortKey)
    {
        ecb.RemoveComponent<EnemyTargetEntity>(sortKey, Self);
        ecb.RemoveComponent<EnemyTargetVelocity>(sortKey, Self);
    }
}




struct ResourceTarget : IComponentData
{
    public Entity ResourceEntity;
}

struct HoldingResource : IComponentData
{
    public Entity ResourceEntity;
}