using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;


[BurstCompile]
[WithAll(typeof(BeeTag))]
[WithNone(typeof(Dying))]
partial struct BeeRandomWalkJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration Config;
    public float DeltaTime;


    [BurstCompile]
    void Execute(ref RandomIndex random, TransformAspect transform, ref Velocity velocity)
    {
        var v = velocity.Value;
        v += (random.random.NextFloat3Direction()) * (Config.flightJitter * DeltaTime);
        v *= (1f - Config.damping);
        velocity.Value = v;
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeDeathSystem))]
[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct BeeRandomWalkSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new BeeRandomWalkJob
        {
            Config = config,
            DeltaTime = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
partial struct BeeAlliesJob : IJobEntity
{
    [DeallocateOnJobCompletion][ReadOnly] public NativeArray<Entity> allies;
    [ReadOnly] public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    [ReadOnly] public BeeConfiguration config;
    public float deltaTime;

    [BurstCompile]
    void Execute(ref Velocity velocity, ref RandomIndex random, in TransformAspect transform)
    {
        // 原始代码中没有进行这个检查，原则上算是一个bug，只不过这个不会出现数组访问越界，但是会除0
        if (allies.Length <= 1)
        {
            return;
        }

        var attractiveFriend = allies[random.random.NextInt(0, allies.Length)];
        var delta = LocalToWorldTransformFromEntity[attractiveFriend].Value.Position - transform.Position;
        float dist = math.length(delta);
        if (dist > 0f)
        {
            velocity.Value += delta * (config.teamAttraction * deltaTime / dist);
        }

        var repellentFriend = allies[random.random.NextInt(0, allies.Length)];
        delta = LocalToWorldTransformFromEntity[repellentFriend].Value.Position - transform.Position;
        dist = math.length(delta);
        if (dist > 0f)
        {
            velocity.Value -= delta * (config.teamRepulsion * deltaTime / dist);
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeDeathSystem))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeAlliesSystem : ISystem
{
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        LocalToWorldTransformFromEntity = SystemAPI.GetComponentLookup<LocalToWorldTransform>(true);
        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    void AlliesFriend(ref SystemState state, int team)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        var q = SystemAPI.QueryBuilder().WithAll<BeeTag, Team>().Build();
        q.SetSharedComponentFilter(new Team { Value = team });
        var allies = q.ToEntityArray(Allocator.TempJob);
        LocalToWorldTransformFromEntity.Update(ref state);
        state.Dependency = new BeeAlliesJob
        {
            allies = allies,
            LocalToWorldTransformFromEntity = LocalToWorldTransformFromEntity,
            config = config,
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }

    public void OnUpdate(ref SystemState state)
    {
        AlliesFriend(ref state, 0);
        AlliesFriend(ref state, 1);
    }
}

[WithNone(typeof(EnemyTargetEntity), typeof(ResourceTarget), typeof(HoldingResource))]
[WithNone(typeof(Dying))]
[BurstCompile]
partial struct BeeNewTargetJob : IJobEntity
{
    [DeallocateOnJobCompletion][ReadOnly] public NativeArray<Entity> enemies;
    [DeallocateOnJobCompletion][ReadOnly] public NativeArray<Entity> resources;
    [ReadOnly] public ComponentLookup<ResourceHolderEntity> ResourceHolderFromEntity;
    public float aggression;
    public EntityCommandBuffer.ParallelWriter ECB;

    [BurstCompile]
    void Execute(ref RandomIndex random,
        in BeeTag b,
        in Entity e,
        [EntityInQueryIndex] int inQueryIndex)
    {
        if (random.random.NextFloat() < aggression)
        {
            // 这里的es.Length > 0判定在现在的模式下可以提到循环外，此处为了和原始代码的对齐保持在这里
            if (enemies.Length > 0)
            {
                // ECB.AddComponent(inQueryIndex, e, new EnemyTarget { BeeEntity = enemies[random.random.NextInt(0, enemies.Length)] });
                EnemyTargetAspect.AddEnemyTarget(ref ECB, inQueryIndex, e,
                    enemies[random.random.NextInt(0, enemies.Length)]);
            }
        }
        else
        {
            if (resources.Length > 0)
            {
                Entity resource = resources[random.random.NextInt(0, resources.Length)];
                // TODO: implement second condition to IsTopOfStack
                if (!ResourceHolderFromEntity.HasComponent(resource) || true)
                {
                    ECB.AddComponent(inQueryIndex, e, new ResourceTarget { ResourceEntity = resource });
                }
            }
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeEnemyTargetSystem))]
[UpdateAfter(typeof(BeeResourceTargetSystem))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeNewTargetSystem : ISystem
{
    ComponentLookup<ResourceHolderEntity> ResourceHolderFromEntity;
    EntityQuery NewTargetJobQuery;
    EntityQuery ResourcesQuery;

    public void OnCreate(ref SystemState state)
    {
        ResourceHolderFromEntity = SystemAPI.GetComponentLookup<ResourceHolderEntity>();
        NewTargetJobQuery = state.GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<RandomIndex>(),
                    ComponentType.ReadOnly<BeeTag>(),
                    ComponentType.ReadOnly<Team>(),
                },
                None = new ComponentType[]
                {
                    typeof(EnemyTargetEntity),
                    typeof(ResourceTarget),
                    typeof(HoldingResource),
                    typeof(Dying)
                }
            }
        );
        ResourcesQuery = SystemAPI.QueryBuilder().WithAll<ResourceTag>().Build();

        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    JobHandle TargetEnemyTeam(ref SystemState state, ref EntityQuery enemyQuery, int aliasTeam, int enemyTeam,
        float aggression)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        enemyQuery.SetSharedComponentFilter(new Team { Value = enemyTeam });
        var enemies = enemyQuery.ToEntityArray(Allocator.TempJob);
        var resources = ResourcesQuery.ToEntityArray(Allocator.TempJob);
        NewTargetJobQuery.SetSharedComponentFilter(
            new Team { Value = aliasTeam }
        );
        return new BeeNewTargetJob
        {
            enemies = enemies,
            resources = resources,
            ResourceHolderFromEntity = ResourceHolderFromEntity,
            aggression = aggression,
            ECB = ecb.AsParallelWriter()
        }.ScheduleParallel(NewTargetJobQuery, state.Dependency);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var beeQuery = SystemAPI.QueryBuilder().WithAll<BeeTag, Team>().WithNone<Dying>().Build();
        ResourceHolderFromEntity.Update(ref state);

        state.Dependency = JobHandle.CombineDependencies(
            TargetEnemyTeam(ref state, ref beeQuery, 0, 1, config.aggression),
            TargetEnemyTeam(ref state, ref beeQuery, 1, 0, config.aggression)
        );
    }
}

[BurstCompile]
partial struct EnemyTargetVelocityUpdateJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Velocity> VelocityFromEntity;

    [BurstCompile]
    void Execute(ref EnemyTargetAspect target)
    {
        if (VelocityFromEntity.HasComponent(target.BeeEntity))
        {
            target.Velocity = VelocityFromEntity[target.BeeEntity].Value;
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeEnemyTargetSystem))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeEnemyTargetVelocityUpdateSystem : ISystem
{
    ComponentLookup<Velocity> VelocityFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        VelocityFromEntity.Update(ref state);

        state.Dependency = new EnemyTargetVelocityUpdateJob
        {
            VelocityFromEntity = VelocityFromEntity
        }.ScheduleParallel(state.Dependency);
    }
}

[WithNone(typeof(Dying))]
[WithAll(typeof(BeeTag))]
[BurstCompile]
partial struct BeeEnemyTargetJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    [ReadOnly] public ComponentLookup<Dying> DeathFromEntity;

    [ReadOnly] public BeeConfiguration config;
    [ReadOnly] public ParticleSpawner particleSpawner;
    public EntityCommandBuffer.ParallelWriter ecb;
    public float deltaTime;
    public int particleCount;

    [BurstCompile]
    void Execute(ref RandomIndex random,
        ref Attacking attacking,
        ref Velocity velocity,
        in TransformAspect transform,
        in EnemyTargetAspect enemyTargetAspect,
        in Entity beeEntity,
        [EntityInQueryIndex] int inQueryIndex)
    {
        // 备注：大问题 - ECS没有引用完整性的保证，对于可能被Destory的Entity，引用的地方会有运行时异常
        // 比如如下的SystemAPI.Exists如果不检查，LocalToWorldTransformFromEntity就运行时异常了
        // 方案1：添加防御性检查，但是其实会让系统留下不正确的状态，即破坏了一致性，认为不是好方案
        // 方案2：在Destory Entity的时候删除所有引用，保证系统的一致性
        // 由于没有反向index，方案2需要完整遍历所有Entity，性能不合适，因此这里用了方案1

        attacking.isAttacking = false;
        if ((!LocalToWorldTransformFromEntity.HasComponent(enemyTargetAspect.BeeEntity)) ||
            DeathFromEntity.HasComponent(enemyTargetAspect.BeeEntity))
        {
            enemyTargetAspect.RemoveEnemyTarget(ref ecb, inQueryIndex);
            // ecb.RemoveComponent<EnemyTargetEntity>(inQueryIndex, beeEntity);
            ecb.SetComponentEnabled<EnemyTargetEntity>(inQueryIndex, beeEntity, false);
            // ecb.RemoveComponent<EnemyTargetAspect>(inQueryIndex, beeEntity);
        }
        else
        {
            var enemyPosition = LocalToWorldTransformFromEntity[enemyTargetAspect.BeeEntity].Value.Position;
            var delta = enemyPosition - transform.Position;
            var distance = math.length(delta);
            var normalizedDelta = delta / distance;
            if (distance > config.attackDistance)
            {
                velocity.Value += normalizedDelta * (config.chaseForce * deltaTime);
            }
            else
            {
                attacking.isAttacking = true;
                velocity.Value += normalizedDelta * (config.attackForce * deltaTime);
                if (distance < config.hitDistance)
                {
                    particleSpawner.SpawnParticleBlood(ref random.random,
                        inQueryIndex,
                        ecb,
                        enemyPosition,
                        velocity.Value * .5f, particleCount, float3.zero, 6f);

                    ecb.AddComponent(inQueryIndex, enemyTargetAspect.BeeEntity, new Dying { Timer = 1f });
                    ecb.SetComponent(inQueryIndex, enemyTargetAspect.BeeEntity, new Velocity
                    {
                        Value = enemyTargetAspect.Velocity * .5f
                    });

                    enemyTargetAspect.RemoveEnemyTarget(ref ecb, inQueryIndex);
                    // ecb.RemoveComponent<EnemyTargetEntity>(inQueryIndex, beeEntity);
                    // ecb.SetComponentEnabled<EnemyTargetEntity>(inQueryIndex, beeEntity, false);
                }
            }
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeEnemyTargetSystem : ISystem
{
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Dying> DeathFromEntity;
    EntityQuery ParticleQuery;

    public void OnCreate(ref SystemState state)
    {
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        DeathFromEntity = state.GetComponentLookup<Dying>(true);
        ParticleQuery = state.GetEntityQuery(typeof(ParticleTag));

        state.RequireForUpdate<BeeConfiguration>();
        state.RequireForUpdate<ParticleConfiguration>();
        state.RequireForUpdate<ParticleSpawnData>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var particleConfig = SystemAPI.GetSingleton<ParticleConfiguration>();
        var particleSpawn = SystemAPI.GetSingleton<ParticleSpawnData>();
        var particleSpawner = new ParticleSpawner
        {
            spawn = particleSpawn
        };
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        LocalToWorldTransformFromEntity.Update(ref state);
        DeathFromEntity.Update(ref state);

        var deltaTime = SystemAPI.Time.DeltaTime;
        var particleCount = ParticleQuery.CalculateEntityCount();
        // UnityEngine.Debug.Log(particleCount);

        state.Dependency = new BeeEnemyTargetJob
        {
            LocalToWorldTransformFromEntity = LocalToWorldTransformFromEntity,
            DeathFromEntity = DeathFromEntity,
            particleSpawner = particleSpawner,
            config = config,
            ecb = ecb.AsParallelWriter(),
            deltaTime = deltaTime,
            particleCount = particleCount > particleConfig.maxParticleCount ? 0 : particleConfig.beeAttackParticleCount,
        }.ScheduleParallel(state.Dependency);
    }
}

[WithNone(typeof(Dying))]
[WithNone(typeof(EnemyTargetEntity))]
[BurstCompile]
partial struct BeeResourceTargetJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration config;
    [ReadOnly] public ComponentLookup<ResourceHolderEntity> ResourceHolderEntityFromEntity;
    [ReadOnly] public ComponentLookup<ResourceHolderTeam> ResourceHolderTeamFromEntity;
    [ReadOnly] public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    [ReadOnly] public ComponentLookup<Stacked> StackedFromEntity;

    [ReadOnly] public ComponentLookup<ResourceTag> ResourceComponentFromEntity;

    public EntityCommandBuffer.ParallelWriter ecb;
    public float DeltaTime;


    [BurstCompile]
    void Execute(
        ref Velocity velocity,
        in ResourceTarget resourceTarget,
        in Team selfTeam,
        in TransformAspect transform,
        in Entity beeEntity,
        in BeeTag bee,
        [EntityInQueryIndex] int inQueryIndex)
    {
        // Resource resource = bee.resourceTarget;
        var resourceEntity = resourceTarget.ResourceEntity;
        // if (resource.holder == null)
        if (!ResourceHolderEntityFromEntity.HasComponent(resourceEntity))
        {
            // if (resource.dead)
            if (!ResourceComponentFromEntity.HasComponent(resourceEntity))
            {
                //     bee.resourceTarget = null;
                ecb.RemoveComponent<ResourceTarget>(inQueryIndex, beeEntity);
            }
            else
            {
                // else if (resourceEntity.stacked && ResourceSystem.IsTopOfStack(resourceEntity) == false)
                if (false)
                {
                    ecb.RemoveComponent<ResourceTarget>(inQueryIndex, beeEntity);
                }
                else
                {
                    var resourcePosition = LocalToWorldTransformFromEntity[resourceEntity].Value.Position;
                    var delta = resourcePosition - transform.Position;
                    float dist = math.length(delta);
                    if (dist > config.grabDistance)
                    {
                        velocity.Value += (delta / dist) * (config.chaseForce * DeltaTime);
                    }
                    // else if (resourceEntity.stacked)
                    else if (StackedFromEntity.HasComponent(resourceEntity))
                    {
                        ecb.AddComponent(inQueryIndex, resourceEntity, new ResourceHolderEntity
                        {
                            Holder = beeEntity
                        });
                        ecb.AddComponent(inQueryIndex, resourceEntity, new ResourceHolderTeam
                        {
                            Team = selfTeam.Value
                        });
                        ecb.RemoveComponent<Stacked>(inQueryIndex, resourceEntity);
                        ecb.AddComponent(inQueryIndex, beeEntity, new HoldingResource
                        {
                            ResourceEntity = resourceEntity
                        });
                        ecb.RemoveComponent<ResourceTarget>(inQueryIndex, beeEntity);
                    }
                }
            }
        }
        else
        {
            var resourceHolder = ResourceHolderEntityFromEntity[resourceEntity].Holder;
            if (resourceHolder == beeEntity)
            {
            }
            else
            {

                // 无法在ParallelJob下直接执行如下操作，会提示EntityManager会被修改
                if (ResourceHolderTeamFromEntity[resourceEntity].Team != selfTeam.Value)
                {
                    // TODO: 如果已经存在EnemyTargetComponent会运行时异常吗
                    EnemyTargetAspect.AddEnemyTarget(ref ecb, inQueryIndex, beeEntity, resourceHolder);
                }
                else
                {
                    // bee.resourceTarget = null;
                    ecb.RemoveComponent<ResourceTarget>(inQueryIndex, beeEntity);
                }
            }
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeResourceTargetSystem : ISystem
{
    ComponentLookup<ResourceHolderEntity> ResourceHolderEntityFromEntity;
    ComponentLookup<ResourceHolderTeam> ResourceHolderTeamFromEntity;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Stacked> StackedFromEntity;
    ComponentLookup<ResourceTag> ResourceComponentFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        ResourceHolderEntityFromEntity = state.GetComponentLookup<ResourceHolderEntity>(true);
        ResourceHolderTeamFromEntity = state.GetComponentLookup<ResourceHolderTeam>(true);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        StackedFromEntity = state.GetComponentLookup<Stacked>(true);
        ResourceComponentFromEntity = state.GetComponentLookup<ResourceTag>(true);

        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecb = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        ResourceHolderEntityFromEntity.Update(ref state);
        ResourceHolderTeamFromEntity.Update(ref state);
        LocalToWorldTransformFromEntity.Update(ref state);
        StackedFromEntity.Update(ref state);
        ResourceComponentFromEntity.Update(ref state);
        var deltaTime = SystemAPI.Time.DeltaTime;


        state.Dependency = new BeeResourceTargetJob
        {
            config = config,
            ResourceHolderEntityFromEntity = ResourceHolderEntityFromEntity,
            ResourceHolderTeamFromEntity = ResourceHolderTeamFromEntity,
            LocalToWorldTransformFromEntity = LocalToWorldTransformFromEntity,
            StackedFromEntity = StackedFromEntity,
            ResourceComponentFromEntity = ResourceComponentFromEntity,
            ecb = ecb.AsParallelWriter(),
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}


[WithNone(typeof(Dying))]
[BurstCompile]
partial struct BeeHoldingResourceTowardsHiveJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ecb;
    [ReadOnly] public BeeConfiguration config;
    [ReadOnly] public FieldComponent field;
    [ReadOnly] public float DeltaTime;

    [BurstCompile]
    void Execute([Unity.Entities.EntityInQueryIndex] int inQueryIndex,
        ref Velocity velocity,
        in BeeTag bee,
        in Team team,
        in TransformAspect transform,
        in HoldingResource holdingResource,
        in Entity beeEntity)
    {
        var targetPos = field.TargetPosition(transform.Position, team.Value);
        var delta = targetPos - transform.Position;
        var dist = math.length(delta);
        if (dist < 1f)
        {
            // resourceEntity.holder = null;
            // bee.resourceTarget = null;
            ecb.RemoveComponent<HoldingResource>(inQueryIndex, beeEntity);
            ecb.RemoveComponent<ResourceHolderEntity>(inQueryIndex, holdingResource.ResourceEntity);
            ecb.RemoveComponent<ResourceHolderTeam>(inQueryIndex, holdingResource.ResourceEntity);
        }
        else
        {
            velocity.Value += (delta / dist) * (config.carryForce * DeltaTime);
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeHoldingResourceTowardsHiveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
        state.RequireForUpdate<FieldComponent>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecb = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var field = SystemAPI.GetSingleton<FieldComponent>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new BeeHoldingResourceTowardsHiveJob
        {
            ecb = ecb.AsParallelWriter(),
            config = config,
            field = field,
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}


[BurstCompile]
partial struct BeeMoveJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration config;
    [ReadOnly] public ResourceConfiguration resourceConfig;
    [ReadOnly] public FieldComponent field;

    [ReadOnly] public ComponentLookup<HoldingResource> HoldingResourceFromEntity;
    public float deltaTime;


    [BurstCompile]
    void Execute(ref Velocity velocity,
        ref TransformAspect transform,
        in BeeTag bee,
        in Entity beeEntity)
    {
        var v = velocity.Value;
        transform.Position += deltaTime * v;
        var position = transform.Position;

        if (math.abs(position.x) > field.Size.x * .5f)
        {
            position.x = (field.Size.x * .5f) * math.sign(position.x);
            v.x *= -.5f;
            v.y *= .8f;
            v.z *= .8f;
        }

        if (math.abs(position.z) > field.Size.z * .5f)
        {
            position.z = (field.Size.z * .5f) * math.sign(position.z);
            v.z *= -.5f;
            v.x *= .8f;
            v.y *= .8f;
        }

        var resourceModifier = HoldingResourceFromEntity.HasComponent(beeEntity) ? resourceConfig.resourceSize : 0f;
        if (math.abs(position.y) > field.Size.y * .5f - resourceModifier)
        {
            position.y = (field.Size.y * .5f - resourceModifier) * math.sign(position.y);
            v.y *= -.5f;
            v.z *= .8f;
            v.x *= .8f;
        }

        transform.Position = position;
        velocity.Value = v;
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeRandomWalkSystem))]
[UpdateBefore(typeof(BeeDeathSystem))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeMoveSystem : ISystem
{
    ComponentLookup<HoldingResource> HoldingResourceFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        HoldingResourceFromEntity = state.GetComponentLookup<HoldingResource>();

        state.RequireForUpdate<BeeConfiguration>();
        state.RequireForUpdate<ResourceConfiguration>();
        state.RequireForUpdate<FieldComponent>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var resourceConfig = SystemAPI.GetSingleton<ResourceConfiguration>();
        var field = SystemAPI.GetSingleton<FieldComponent>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        HoldingResourceFromEntity.Update(ref state);

        state.Dependency = new BeeMoveJob
        {
            config = config,
            resourceConfig = resourceConfig,
            field = field,
            deltaTime = deltaTime,
            HoldingResourceFromEntity = HoldingResourceFromEntity,
        }.ScheduleParallel(state.Dependency);
    }
}

struct BeeSpawnData : IComponentData
{
    public Entity BeePrefab;
}

[BurstCompile]
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(InitializationSystemGroup))]
partial struct BeeSpawnPrefabUpdateSystem : ISystem
{
    [BurstCompile]
    public static void UpdateBeePrefab(ref EntityCommandBuffer.ParallelWriter ECB, in BeeConfiguration config, int sortKey)
    {
        var instance = config.BeePrefab;

        ECB.AddComponent<Velocity>(sortKey, instance);

        ECB.AddComponent<URPMaterialPropertyBaseColor>(sortKey, instance);
        ECB.AddComponent(sortKey, instance, new BeeTag());
        ECB.AddComponent(sortKey, instance, new BeeSize
        {
            size = 1f,
        });
        // ECB.AddComponent(instance, new SmoothPositionVelocityAspect { });
        SmoothPositionVelocityAspect.AddSmoothPositionVelocity(ref ECB, instance, sortKey);
        ECB.AddComponent(sortKey, instance, new PostTransformMatrix { Value = float4x4.identity });
        ECB.AddComponent<RandomIndex>(sortKey, instance);
        ECB.AddComponent(sortKey, instance, new Attacking { isAttacking = false });

        // HACK: 尝试添加EnemyTargetVelociy，减少之后的AddComponent/RemoveComponent
        ECB.AddComponent(sortKey, instance, new EnemyTargetVelocity { Velocity = float3.zero });
        ECB.AddComponent<EnemyTargetEntity>(sortKey, instance);
        ECB.SetComponentEnabled<EnemyTargetEntity>(sortKey, instance, false);
    }
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var field = SystemAPI.GetSingleton<FieldComponent>();
        var sortKey = 0;

        UpdateBeePrefab(ref ecb, config, sortKey);

        var beeSpawn = ecb.CreateEntity(sortKey);
        ecb.AddComponent(sortKey, beeSpawn, new BeeSpawnData { BeePrefab = config.BeePrefab });

        state.Enabled = false;
    }
}

[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct BeeSpawnSystem : ISystem
{



    [BurstCompile]
    public static void SpawnBee(ref EntityCommandBuffer.ParallelWriter ECB, ref Unity.Mathematics.Random random,
        in float3 pos, int team, in BeeConfiguration config, in BeeSpawnData spawn, int sortKey)
    {
        var instance = ECB.Instantiate(sortKey, spawn.BeePrefab);
        var size = random.NextFloat(config.minBeeSize, config.maxBeeSize);
        ECB.SetComponent(sortKey, instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(pos).ApplyScale(size)
        });

        var velocity = random.NextFloat3Direction() * config.maxSpawnSpeed;
        ECB.SetComponent(sortKey, instance, new Velocity { Value = velocity });

        ECB.AddSharedComponent(sortKey, instance, new Team { Value = team });
        var color = team == 0 ? config.teamAColor : config.teamBColor;
        ECB.SetComponent(sortKey, instance,
            new URPMaterialPropertyBaseColor { Value = math.float4(color.r, color.g, color.b, color.a) });
        // ECB.AddComponent(sortKey, instance, new BeeTag());
        // ECB.AddComponent(sortKey, instance, new BeeSize
        // {
            // size = 1f,
        // });
        // ECB.AddComponent(instance, new SmoothPositionVelocityAspect { });
        // SmoothPositionVelocityAspect.AddSmoothPositionVelocity(ref ECB, instance, sortKey);
        // ECB.AddComponent(sortKey, instance, new PostTransformMatrix { Value = float4x4.identity });
        ECB.SetComponent(sortKey, instance,
            new RandomIndex { random = Unity.Mathematics.Random.CreateFromIndex(random.NextUInt()) });
        // ECB.AddComponent(sortKey, instance, new Attacking { isAttacking = false });

        // HACK: 尝试添加EnemyTargetVelociy，减少之后的AddComponent/RemoveComponent
        // ECB.AddComponent(sortKey, instance, new EnemyTargetVelocity { Velocity = float3.zero });
        // ECB.AddComponent<EnemyTargetEntity>(sortKey, instance);
        // ECB.SetComponentEnabled<EnemyTargetEntity>(sortKey, instance, false);
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
        state.RequireForUpdate<FieldComponent>();
        state.RequireForUpdate<BeeSpawnData>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var spawn = SystemAPI.GetSingleton<BeeSpawnData>();

        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var field = SystemAPI.GetSingleton<FieldComponent>();


        var random = Unity.Mathematics.Random.CreateFromIndex(233);
        for (int i = 0; i < config.startBeeCount; i++)
        {
            int team = i % 2;

            Vector3 pos = Vector3.right * (-field.Size.x * .4f + field.Size.x * .8f * team);
            SpawnBee(ref ecb, ref random, pos, team, config, spawn, i);
        }

        state.Enabled = false;
    }
}

[BurstCompile]
[WithAll(typeof(BeeTag))]
[WithAll(typeof(Dying))]
partial struct DyingBeeRemoveHoldingResourceJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<ResourceHolderEntity> ResourceHolderFromEntity;
    public EntityCommandBuffer.ParallelWriter ecb;
    [BurstCompile]
    void Execute(in HoldingResource holdingResource, in Entity beeEntity, [EntityInQueryIndex] int inQueryIndex)
    {
        var resourceEntity = holdingResource.ResourceEntity;
        ecb.RemoveComponent<HoldingResource>(inQueryIndex, beeEntity);
        if (ResourceHolderFromEntity.HasComponent(resourceEntity) && ResourceHolderFromEntity[resourceEntity].Holder == beeEntity)
        {
            ecb.RemoveComponent<ResourceHolderEntity>(inQueryIndex, resourceEntity);
            ecb.RemoveComponent<ResourceHolderTeam>(inQueryIndex, resourceEntity);
        }
    }
}

[BurstCompile]
[WithAll(typeof(BeeTag))]
partial struct BeeDeathJob : IJobEntity
{
    [ReadOnly] public FieldComponent fieldConfiguration;
    public EntityCommandBuffer.ParallelWriter ecb;
    public float deltaTime;
    public ParticleSpawner ParticleSpawner;
    public int ParticleCount;

    [BurstCompile]
    void Execute(ref RandomIndex random,
        ref Velocity velocity,
        ref Dying death,
        in Entity e,
        in TransformAspect transform,
        [EntityInQueryIndex] int inQueryIndex)
    {
        if (random.random.NextFloat() < (death.Timer - .5f) * .5f)
        {
            ParticleSpawner.SpawnParticleBlood(ref random.random, inQueryIndex, ecb, transform.Position,
                velocity.Value, ParticleCount, float3.zero, float3.zero);
        }

        var v = velocity.Value;
        v.y += fieldConfiguration.Gravity * deltaTime;
        velocity.Value = v;

        death.Timer -= deltaTime / 10f;
        if (death.Timer < 0f)
        {
            ecb.DestroyEntity(inQueryIndex, e);
            // ecb.AddComponent<Dead>(inQueryIndex, e);
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeNewTargetSystem))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct BeeDeathSystem : ISystem
{
    EntityQuery ParticleQuery;
    EntityQuery DeadBeeQuery;

    ComponentLookup<ResourceHolderEntity> ResourceHolderFromEntity;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FieldComponent>();
        state.RequireForUpdate<ParticleConfiguration>();
        state.RequireForUpdate<ParticleSpawnData>();
        ResourceHolderFromEntity = state.GetComponentLookup<ResourceHolderEntity>();
        ParticleQuery = state.GetEntityQuery(typeof(ParticleTag));
        DeadBeeQuery = state.GetEntityQuery(typeof(BeeTag), typeof(Dead));
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var particleConfig = SystemAPI.GetSingleton<ParticleConfiguration>();
        var particleSpawn = SystemAPI.GetSingleton<ParticleSpawnData>();
        var fieldConfiguration = SystemAPI.GetSingleton<FieldComponent>();

        var ecb = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var particleSpawner = new ParticleSpawner { spawn = particleSpawn };
        var particleCount = ParticleQuery.CalculateEntityCount();
        ResourceHolderFromEntity.Update(ref state);

        var deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new DyingBeeRemoveHoldingResourceJob
        {
            ecb = ecb,
            ResourceHolderFromEntity = ResourceHolderFromEntity
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new BeeDeathJob
        {
            fieldConfiguration = fieldConfiguration,
            ecb = ecb,
            deltaTime = deltaTime,

            ParticleSpawner = particleSpawner,
            ParticleCount = particleCount > particleConfig.maxParticleCount ? 0 : particleConfig.beeDeathParticleCount,
        }.ScheduleParallel(state.Dependency);

    }
}