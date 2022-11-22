using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

public struct BeeComponent : IComponentData
{
    public float3 smoothPosition;
    public float3 smoothDirection;
    public int team;
    public float size;
    public bool isAttacking;
    public bool isHoldingResource;
    public int index;
}

public struct BeeSpawn : IComponentData
{
    public float3 Position;
    public int team;
}


public struct SmoothPositionVelociy : IComponentData
{
    public float3 Position;
    public float3 Velocity;
}

public partial struct Team : ISharedComponentData
{
    public int Value;
}

public partial struct Velocity : IComponentData
{
    public float3 Value;
}

// alive bee有四种互斥的状态
// - idle
// - EnemyTarget
// - ResourceTarget
// - HoldingResource




public partial struct Dying : IComponentData
{
    public float Timer;
}

public partial struct EnemyTarget : IComponentData
{
    public Entity BeeEntity;
}

public partial struct ResourceTarget : IComponentData
{
    public Entity ResourceEntity;
}

public partial struct HoldingResource : IComponentData
{
    public Entity ResourceEntity;
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeDeathSystem))]
public partial struct BeeRandomWalkSystem : ISystem
{
    public Unity.Mathematics.Random random;

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (bee, transform, velocity) in SystemAPI.Query<
            RefRW<BeeComponent>,
            TransformAspect,
            RefRW<Velocity>>().WithNone<Dying>())
        {
            bee.ValueRW.isAttacking = false;
            bee.ValueRW.isHoldingResource = false;
            var v = velocity.ValueRO.Value;
            v += (random.NextFloat3Direction()) * (config.flightJitter * deltaTime);
            v *= (1f - config.damping);

            velocity.ValueRW.Value = v;
        }
    }
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(42);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeDeathSystem))]
public partial struct BeeAlliesSystem : ISystem
{
    Unity.Mathematics.Random random;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(42);
        LocalToWorldTransformFromEntity = SystemAPI.GetComponentLookup<LocalToWorldTransform>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    void AlliesFriend(ref SystemState state, int team)
    {

        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        var q = SystemAPI.QueryBuilder().WithAll<BeeComponent, Team>().Build();
        q.SetSharedComponentFilter(new Team { Value = team });
        using var allies = q.ToEntityArray(Allocator.Temp);
        LocalToWorldTransformFromEntity.Update(ref state);

        foreach (var (bee, transform, velocity) in SystemAPI.Query<BeeComponent, TransformAspect, RefRW<Velocity>>().WithSharedComponentFilter(new Team { Value = team }))
        {
            // 原始代码中没有进行这个检查，原则上算是一个bug，只不过这个不会出现数组访问越界，但是会除0
            if (allies.Length <= 1)
            {
                continue;
            }
            var attractiveFriend = allies[random.NextInt(0, allies.Length)];
            var delta = LocalToWorldTransformFromEntity[attractiveFriend].Value.Position - transform.Position;
            float dist = math.length(delta);
            if (dist > 0f)
            {
                velocity.ValueRW.Value += delta * (config.teamAttraction * deltaTime / dist);
            }

            var repellentFriend = allies[random.NextInt(0, allies.Length)];
            delta = LocalToWorldTransformFromEntity[repellentFriend].Value.Position - transform.Position;
            dist = math.length(delta);
            if (dist > 0f)
            {
                velocity.ValueRW.Value -= delta * (config.teamRepulsion * deltaTime / dist);
            }
        }


    }

    public void OnUpdate(ref SystemState state)
    {
        AlliesFriend(ref state, 0);
        AlliesFriend(ref state, 1);
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeEnemyTargetSystem))]
[UpdateAfter(typeof(BeeResourceTargetSystem))]
public partial struct BeeNewTargetSystem : ISystem
{
    Unity.Mathematics.Random random;
    ComponentLookup<ResourceHolder> ResourceHolderFromEntity;


    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(233);
        ResourceHolderFromEntity = SystemAPI.GetComponentLookup<ResourceHolder>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    void TargetEnemyTeam(ref SystemState state, ref EntityCommandBuffer ECB, ref EntityQuery enemyQuery, int aliasTeam, int enemyTeam, float aggression, in NativeArray<Entity> resources)
    {
        enemyQuery.SetSharedComponentFilter(new Team { Value = enemyTeam });
        ResourceHolderFromEntity.Update(ref state);
        using var es = enemyQuery.ToEntityArray(Allocator.Temp);
        foreach (var (b, e) in SystemAPI.Query<BeeComponent>()
                                                .WithNone<EnemyTarget, ResourceTarget>()
                                                .WithEntityAccess()
                                                .WithSharedComponentFilter(new Team { Value = aliasTeam }))
        {
            if (random.NextFloat() < aggression)
            {
                // 这里的es.Length > 0判定在现在的模式下可以提到循环外，此处为了和原始代码的对齐保持在这里
                if (es.Length > 0)
                {
                    ECB.AddComponent(e, new EnemyTarget { BeeEntity = es[random.NextInt(0, es.Length)] });
                }
            }
            else
            {
                if (resources.Length > 0)
                {
                    Entity resource = resources[random.NextInt(0, resources.Length)];
                    // TODO: implement second condition to IsTopOfStack
                    if ((!ResourceHolderFromEntity.HasComponent(resource)) || true)
                    {
                        ECB.AddComponent(e, new ResourceTarget { ResourceEntity = resource });
                    }
                }

                //             bee.resourceTarget = ResourceSystem.TryGetRandomResource();

                // inlined TryGetRandomResource
                // if (instance.resources.Count == 0)
                // {
                //     return null;
                // }
                // else
                // {
                //     Resource resource = instance.resources[Random.Range(0, instance.resources.Count)];
                //     // int stackHeight = instance.stackHeights[resource.gridX,resource.gridY];
                //     // if (resource.holder == null || resource.stackIndex == stackHeight - 1)
                //     if (resource.holder == null)
                //     {
                //         return resource;
                //     }
                //     else
                //     {
                //         return null;
                //     }
                // }


            }
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var beeQuery = SystemAPI.QueryBuilder().WithAll<BeeComponent, Team>().Build();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        using var resources = SystemAPI.QueryBuilder().WithAll<ResourceComponent>().Build().ToEntityArray(Allocator.Temp);

        TargetEnemyTeam(ref state, ref ecb, ref beeQuery, 0, 1, config.aggression, resources);
        TargetEnemyTeam(ref state, ref ecb, ref beeQuery, 1, 0, config.aggression, resources);
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct BeeEnemyTargetSystem : ISystem
{
    Unity.Mathematics.Random random;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Velocity> VelocityFromEntity;
    ComponentLookup<Dying> DeathFromEntity;


    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);
        DeathFromEntity = state.GetComponentLookup<Dying>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {

        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        LocalToWorldTransformFromEntity.Update(ref state);
        VelocityFromEntity.Update(ref state);
        DeathFromEntity.Update(ref state);

        var deltaTime = SystemAPI.Time.DeltaTime;



        foreach (var (bee, target, transform, velocity, e) in SystemAPI.Query<RefRW<BeeComponent>, EnemyTarget, TransformAspect, RefRW<Velocity>>().WithNone<Dying>().WithEntityAccess())
        {
            // 备注：大问题 - ECS没有引用完整性的保证，对于可能被Destory的Entity，引用的地方会有运行时异常
            // 比如如下的SystemAPI.Exists如果不检查，LocalToWorldTransformFromEntity就运行时异常了
            // 方案1：添加防御性检查，但是其实会让系统留下不正确的状态，即破坏了一致性，认为不是好方案
            // 方案2：在Destory Entity的时候删除所有引用，保证系统的一致性
            // 由于没有反向index，方案2需要完整遍历所有Entity，性能不合适，因此这里用了方案1
            if ((!SystemAPI.Exists(target.BeeEntity)) || DeathFromEntity.HasComponent(target.BeeEntity))
            {
                ecb.RemoveComponent<EnemyTarget>(e);
            }
            else
            {
                var enemyPosition = LocalToWorldTransformFromEntity[target.BeeEntity].Value.Position;
                var delta = enemyPosition - transform.Position;
                var distance = math.length(delta);
                var normalizedDelta = delta / distance;
                if (distance > config.attackDistance)
                {
                    velocity.ValueRW.Value += normalizedDelta * (config.chaseForce * deltaTime);
                }
                else
                {
                    bee.ValueRW.isAttacking = true;
                    velocity.ValueRW.Value += normalizedDelta * (config.attackForce * deltaTime);
                    if (distance < config.hitDistance)
                    {
                        // ParticleManager.SpawnParticle(bee.enemyTarget.position, ParticleType.Blood, bee.velocity * .35f, 2f, 6);
                        ecb.AddComponent(target.BeeEntity, new Dying { Timer = 1f });

                        ecb.SetComponent(target.BeeEntity, new Velocity
                        {
                            Value = VelocityFromEntity[target.BeeEntity].Value * .5f
                        });

                        ecb.RemoveComponent<EnemyTarget>(e);
                    }
                }
            }
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct BeeResourceTargetSystem : ISystem
{
    Unity.Mathematics.Random random;

    ComponentLookup<ResourceHolder> ResourceHolderFromEntity;


    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(233);
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);
        ResourceHolderFromEntity.Update(ref state);


        foreach (var (bee, resourceTarget, selfTeam, transform, velocity, beeEntity) in SystemAPI.Query<BeeComponent, ResourceTarget, Team, TransformAspect, RefRW<Velocity>>()
                                        .WithNone<Dying>()
                                        .WithNone<EnemyTarget>()
                                        .WithEntityAccess())
        {
            ecb.RemoveComponent<ResourceTarget>(beeEntity);
            return;
            // Resource resource = bee.resourceTarget;
            Entity resourceEntity = resourceTarget.ResourceEntity;
            // if (resource.holder == null)
            if (!ResourceHolderFromEntity.HasComponent(resourceEntity))
            {
                // if (resource.dead)
                if (false)
                {
                    //     bee.resourceTarget = null;
                    ecb.RemoveComponent<ResourceTarget>(beeEntity);
                }
                // else if (resourceEntity.stacked && ResourceSystem.IsTopOfStack(resourceEntity) == false)
                else if (false)
                {
                    // bee.resourceTarget = null;
                    ecb.RemoveComponent<ResourceTarget>(beeEntity);
                }
                else
                {
                    // delta = resourceEntity.position - bee.position;
                    // float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                    // if (sqrDist > grabDistance * grabDistance)
                    // {
                    //     bee.velocity += delta * (chaseForce * deltaTime / Mathf.Sqrt(sqrDist));
                    // }
                    // else if (resourceEntity.stacked)
                    // {
                    //     ResourceSystem.GrabResource(bee, resourceEntity);
                    // }
                }
            }
            else
            {
                Entity resourceHolderEntity = ResourceHolderFromEntity[resourceEntity].Holder;
                if (resourceHolderEntity == beeEntity)
                {

                }
                else
                {
                    var resourceHolderTeam = state.EntityManager.GetSharedComponent<Team>(resourceHolderEntity).Value;
                    if (resourceHolderTeam != selfTeam.Value)
                    {
                        // TODO: 如果已经存在EnemyTargetComponent会运行时异常吗
                        ecb.AddComponent(beeEntity, new EnemyTarget { BeeEntity = resourceHolderEntity });
                    }
                    else
                    {
                        // bee.resourceTarget = null;
                        ecb.RemoveComponent<ResourceTarget>(beeEntity);
                    }
                }
            }

        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct BeeHoldingResourceTowardsHiveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var (bee, transform, velocity, beeEntity) in SystemAPI.Query<BeeComponent, TransformAspect, RefRW<Velocity>>()
                                       .WithNone<Dying>()
                                       .WithEntityAccess())
        {
            var config = SystemAPI.GetSingleton<BeeConfiguration>();
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                               .CreateCommandBuffer(state.WorldUnmanaged);
            // TODO: 改为可读性更好的模式，避免bea.team作为int作用
            float3 targetPos = math.float3(-Field.size.x * .45f + Field.size.x * .9f * bee.team, 0f, transform.Position.z);
            var delta = targetPos - transform.Position;
            var dist = math.length(delta);
            velocity.ValueRW.Value += delta / dist * config.carryForce * SystemAPI.Time.DeltaTime;
            if (dist < 1f)
            {
                // resourceEntity.holder = null;
                // bee.resourceTarget = null;
                ecb.RemoveComponent<HoldingResource>(beeEntity);
            }
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeRandomWalkSystem))]
[UpdateBefore(typeof(BeeDeathSystem))]
public partial struct BeeMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var resourceConfig = SystemAPI.GetSingleton<ResourceConfiguration>();
        foreach (var (bee, velocity, transform, smoothPosition) in SystemAPI.Query<BeeComponent, RefRW<Velocity>, TransformAspect, RefRW<SmoothPositionVelociy>>())
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var v = velocity.ValueRO.Value;
            transform.Position += deltaTime * v;
            var position = transform.Position;

            if (math.abs(position.x) > Field.size.x * .5f)
            {
                position.x = (Field.size.x * .5f) * math.sign(position.x);
                v.x *= -.5f;
                v.y *= .8f;
                v.z *= .8f;
            }
            if (math.abs(position.z) > Field.size.z * .5f)
            {
                position.z = (Field.size.z * .5f) * math.sign(position.z);
                v.z *= -.5f;
                v.x *= .8f;
                v.y *= .8f;
            }

            var resourceModifier = bee.isHoldingResource ? resourceConfig.resourceSize : 0f;
            if (math.abs(position.y) > Field.size.y * .5f - resourceModifier)
            {
                position.y = (Field.size.y * .5f - resourceModifier) * math.sign(position.y);
                v.y *= -.5f;
                v.z *= .8f;
                v.x *= .8f;
            }
            transform.Position = position;
            velocity.ValueRW.Value = v;


            // // only used for smooth rotation:
            var oldPosition = smoothPosition.ValueRO.Position;
            var updatedSmoothPosition = bee.isAttacking ? transform.Position :
                math.lerp(oldPosition, transform.Position, deltaTime * config.rotationStiffness);
            smoothPosition.ValueRW.Position = updatedSmoothPosition;
            smoothPosition.ValueRW.Velocity = updatedSmoothPosition - oldPosition;
        }
    }
}

public partial struct BeeSpawnSystem : ISystem
{
    Unity.Mathematics.Random random;

    void SpawnBee(ref EntityCommandBuffer ECB, float3 pos, int team, in Entity prefab, float2 sizeRange, Color team0Color, Color team1Color, float3 maxSpawnSpeed)
    {
        var instance = ECB.Instantiate(prefab);
        var size = random.NextFloat(sizeRange[0], sizeRange[1]);
        ECB.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(pos).ApplyScale(size)
        });

        var velocity = random.NextFloat3Direction() * maxSpawnSpeed;
        ECB.AddComponent(instance, new Velocity { Value = velocity });

        ECB.AddSharedComponent(instance, new Team { Value = team });
        var color = team == 0 ? team0Color : team1Color;
        ECB.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = math.float4(color.r, color.g, color.b, color.a) });
        ECB.AddComponent(instance, new BeeComponent
        {
            size = 1f,
        });
        ECB.AddComponent(instance, new SmoothPositionVelociy { });
        ECB.AddComponent(instance, new PostTransformMatrix { Value = float4x4.identity });
    }
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
        state.EntityManager.CreateArchetype(typeof(BeeSpawn));
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        for (int i = 0; i < config.startBeeCount; i++)
        {
            int team = i % 2;

            Vector3 pos = Vector3.right * (-Field.size.x * .4f + Field.size.x * .8f * team);
            SpawnBee(ref ecb, pos, team, config.BeePrefab, math.float2(config.minBeeSize, config.maxBeeSize), config.teamAColor, config.teamBColor, config.maxSpawnSpeed);
        }

        state.Enabled = false;

    }
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeNewTargetSystem))]
public partial struct BeeDeathSystem : ISystem
{
    Unity.Mathematics.Random random;
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        foreach (var (bee, velocity, death, e) in SystemAPI.Query<RefRW<BeeComponent>, RefRW<Velocity>, RefRW<Dying>>().WithEntityAccess())
        {
            if (random.NextFloat() < (death.ValueRO.Timer - .5f) * .5f)
            {
                // ParticleManager.SpawnParticle(bee.position, ParticleType.Blood, Vector3.zero);
            }

            var v = velocity.ValueRO.Value;
            v.y += Field.gravity * deltaTime;
            velocity.ValueRW.Value = v;

            death.ValueRW.Timer -= deltaTime / 10f;
            if (death.ValueRW.Timer < 0f)
            {
                ecb.DestroyEntity(e);
            }
        }
    }
}

partial struct BeeSmoothMovePresentSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        foreach (var (bee, velocity, transform, smooth, matrix) in SystemAPI.Query<BeeComponent, Velocity, TransformAspect, SmoothPositionVelociy, RefRW<PostTransformMatrix>>().WithNone<Dying>())
        {
            float size = bee.size;
            float3 scale = math.float3(size);
            float stretch = math.max(1f, math.length(velocity.Value) * config.speedStretch);
            scale.z *= stretch;
            scale.x /= (stretch - 1f) / 5f + 1f;
            scale.y /= (stretch - 1f) / 5f + 1f;

            quaternion rotation = quaternion.identity;
            if (math.length(smooth.Velocity) > math.EPSILON)
            {
                rotation = quaternion.LookRotation(smooth.Velocity, Vector3.up);
            }
            matrix.ValueRW.Value = float4x4.TRS(float3.zero, rotation, scale);
        }

        foreach (var (bee, team, dying, smooth, color, matrix) in SystemAPI.Query<BeeComponent, Team, Dying, SmoothPositionVelociy, RefRW<URPMaterialPropertyBaseColor>, RefRW<PostTransformMatrix>>())
        {
            quaternion rotation = quaternion.identity;
            if (math.length(smooth.Velocity) > math.EPSILON)
            {
                rotation = quaternion.LookRotation(smooth.Velocity, Vector3.up);
            }
            matrix.ValueRW.Value = float4x4.TRS(float3.zero, rotation, math.float3(math.sqrt(dying.Timer)));
            var c = team.Value == 0 ? config.teamAColor : config.teamBColor;
            color.ValueRW.Value = math.float4(math.float3(c.r, c.g, c.b) * .75f, 1f);
        }
    }
}