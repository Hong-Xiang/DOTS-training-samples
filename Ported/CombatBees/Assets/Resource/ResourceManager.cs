using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
partial class ResourceSimulationGroup : ComponentSystemGroup
{
}

[BurstCompile]
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(ResourceSimulationGroup))]
partial struct ResourceSpawnSystem : ISystem
{
    bool isFirstFrame;
    Unity.Mathematics.Random random;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        isFirstFrame = true;
        random = Unity.Mathematics.Random.CreateFromIndex(42);

        state.RequireForUpdate<ResourceConfiguration>();
        state.RequireForUpdate<Grid>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    void SpawnResource(ref EntityCommandBuffer ecb, float3 position, float scale, Entity prefab)
    {
        var instance = ecb.Instantiate(prefab);
        ecb.AddComponent(instance, new ResourceTag { });
        ecb.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position).ApplyScale(scale)
        });
        ecb.AddComponent(instance, new Velocity { Value = float3.zero });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var grid = SystemAPI.GetSingleton<Grid>();

        if (isFirstFrame)
        {
            for (int i = 0; i < config.startResourceCount; i++)
            {
                float3 pos =
                    grid.LocalToWorld(
                        random.NextFloat3(
                            math.float3(0.5f - 0.125f, 0.5f, 0f),
                            math.float3(0.5f + 0.125f, 1f, 1f)
                        )
                    );
                SpawnResource(ref ecb, pos, config.resourceSize, config.resourcePrefab);
            }

            isFirstFrame = false;
        }

        // 此处计算由于ECB的lazy性质，是delay了一帧的，也即存在可能性产生超过resourceCount个数的resource
        var resourcesCount = SystemAPI.QueryBuilder().WithAll<ResourceTag>().Build().CalculateEntityCount();

        // if (resources.Count < 1000 && MouseRaycaster.isMouseTouchingField)
        if (resourcesCount < config.maxResourceCount)
        {
            if (Input.GetKey(KeyCode.Mouse0))
            {
                // spawnTimer += SystemAPI.Time.DeltaTime;
                // while (spawnTimer > 1f / config.spawnRate)
                // {
                //     spawnTimer -= 1f / config.spawnRate;
                //     SpawnResource(ref ecb, MouseRaycaster.worldMousePosition, config.resourceSize);
                // }
            }
        }
    }
}

[WithAll(typeof(ResourceTag))]
[WithAll(typeof(ResourceHolderEntity))]
[BurstCompile]
partial struct ResourceFollowHolderJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<BeeSize> BeeSizeFromEntity;
    [ReadOnly] public ComponentLookup<Dying> DyingFromEntity;

    [ReadOnly] [NativeDisableContainerSafetyRestriction]
    public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;

    [ReadOnly] [NativeDisableContainerSafetyRestriction]
    public ComponentLookup<Velocity> VelocityFromEntity;

    public EntityCommandBuffer.ParallelWriter ECB;
    public ResourceConfiguration config;
    public float deltaTime;

    [BurstCompile]
    public void Execute(ref Velocity velocity,
        ref TransformAspect transform,
        in ResourceHolderEntity holder,
        in Entity e,
        [EntityInQueryIndex] int inQueryIndex)
    {
        if (!BeeSizeFromEntity.HasComponent(holder.Holder) || DyingFromEntity.HasComponent(holder.Holder))
        {
            ECB.RemoveComponent<ResourceHolderEntity>(inQueryIndex, e);
            ECB.RemoveComponent<ResourceHolderTeam>(inQueryIndex, e);
        }
        else
        {
            var holderSize = BeeSizeFromEntity[holder.Holder];
            var holderTransform = LocalToWorldTransformFromEntity[holder.Holder];
            var holderPosition = holderTransform.Value.Position;
            var targetPos = holderPosition - math.float3(Vector3.up) * (config.resourceSize + holderSize.size) * .5f;
            transform.Position = math.lerp(transform.Position, targetPos,
                config.carryStiffness * deltaTime);
            var holderVelocity = VelocityFromEntity[holder.Holder];
            velocity.Value = holderVelocity.Value;
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct ResourceFollowHolderSystem : ISystem
{
    ComponentLookup<BeeSize> BeeSizeFromEntity;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Dying> DyingFromEntity;
    ComponentLookup<Velocity> VelocityFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BeeSizeFromEntity = state.GetComponentLookup<BeeSize>(true);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);
        DyingFromEntity = state.GetComponentLookup<Dying>(true);

        state.RequireForUpdate<ResourceConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var deltaTime = SystemAPI.Time.DeltaTime;

        BeeSizeFromEntity.Update(ref state);
        LocalToWorldTransformFromEntity.Update(ref state);
        VelocityFromEntity.Update(ref state);
        DyingFromEntity.Update(ref state);

        state.Dependency = new ResourceFollowHolderJob
        {
            BeeSizeFromEntity = BeeSizeFromEntity,
            LocalToWorldTransformFromEntity = LocalToWorldTransformFromEntity,
            VelocityFromEntity = VelocityFromEntity,
            DyingFromEntity = DyingFromEntity,
            ECB = ecb,
            config = config,
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}


[BurstCompile]
[WithNone(typeof(ResourceHolderEntity))]
[WithNone(typeof(Stacked))]
[WithNone(typeof(Stacking))]
[WithAll(typeof(ResourceTag))]
partial struct ResourceFallenJob : IJobEntity
{
    [ReadOnly] public Grid grid;
    [ReadOnly] public ResourceConfiguration config;
    [ReadOnly] public BeeConfiguration beeConfig;
    [ReadOnly] public BeeSpawnData beeSpawn;

    [ReadOnly] public FieldComponent field;

    [ReadOnly] public NativeArray<int> heightData;
    public ParticleSpawner particleSpawner;
    public EntityCommandBuffer.ParallelWriter ecb;
    public Unity.Mathematics.Random random;
    public float deltaTime;


    [BurstCompile]
    void Execute(ref TransformAspect transform, ref Velocity velocity, in Entity e,
        [EntityInQueryIndex] int inQueryIndex)
    {
        var position = transform.Position;

        position = math.lerp(position, grid.NearestSnappedPos(position), config.snapStiffness * deltaTime);

        var v = velocity.Value;
        v.y += field.Gravity * deltaTime;
        position += v * deltaTime;


        var idx = grid.ToInboundIndex(grid.PositionToIndex(position));
        var stackHeightMap = new NativeArray2DProxy<int>
        {
            shape = math.int2(grid.Shape.x, grid.Shape.z),
            data = heightData
        };

        // 当resource比较多的时候，可能同时存在多个resource在同一个stack上，此时下面的floorY判断逻辑会出问题
        var floorY = grid.bottom + stackHeightMap[idx.x, idx.z] * config.resourceSize;
        // var floorY = grid.bottom + 0;
        for (var j = 0; j < 3; j++)
        {
            if (math.abs(position[j]) > field.Size[j] * .5f)
            {
                position[j] = field.Size[j] * .5f * Mathf.Sign(position[j]);
                v[j] *= -.5f;
                v[(j + 1) % 3] *= .8f;
                v[(j + 2) % 3] *= .8f;
            }
        }

        if (position.y <= floorY)
        {
            position.y = floorY;
            if (math.abs(position.x) > field.Size.x * .4f)
            {
                for (int i = 0; i < 5; i++)
                {
                    particleSpawner.SpawnParticleSpawnFlash(ref random, ecb, inQueryIndex,
                        position + random.NextFloat3Direction(), random.NextFloat3Direction());
                }

                for (int i = 0; i < config.beesPerResource; i++)
                {
                    BeeSpawnSystem.SpawnBee(ref ecb, ref random, position, position.x < 0 ? 0 : 1, beeConfig,
                        beeSpawn,
                        inQueryIndex);
                }

                ecb.DestroyEntity(inQueryIndex, e);
            }
            else
            {
                ecb.AddComponent(inQueryIndex, e, new Stacked
                {
                    Index = stackHeightMap[idx.x, idx.z]
                });
                // stackHeight[idx.x, idx.z]++;
            }
        }

        transform.Position = position;
        velocity.Value = v;
    }
}


[BurstCompile]
[UpdateInGroup(typeof(ResourceSimulationGroup))]
partial struct ResourceFallenSystem : ISystem
{
    Unity.Mathematics.Random random;

    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(42);

        state.RequireForUpdate<FieldComponent>();
        state.RequireForUpdate<Grid>();
        state.RequireForUpdate<ResourceConfiguration>();
        state.RequireForUpdate<BeeConfiguration>();
        state.RequireForUpdate<BeeSpawnData>();
        state.RequireForUpdate<StackHeight>();
        state.RequireForUpdate<ParticleSpawnData>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var grid = SystemAPI.GetSingleton<Grid>();
        var heightData = SystemAPI.GetSingletonBuffer<StackHeight>().Reinterpret<int>().AsNativeArray();


        var beeConfig = SystemAPI.GetSingleton<BeeConfiguration>();
        var beeSpawn = SystemAPI.GetSingleton<BeeSpawnData>();
        var field = SystemAPI.GetSingleton<FieldComponent>();


        var particleSpawner = new ParticleSpawner
        {
            spawn = SystemAPI.GetSingleton<ParticleSpawnData>()
        };

        var deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new ResourceFallenJob
        {
            grid = grid,
            config = config,
            beeConfig = beeConfig,
            beeSpawn = beeSpawn,
            field = field,
            heightData = heightData,
            particleSpawner = particleSpawner,
            ecb = ecb,
            random = random,
            deltaTime = deltaTime
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ResourceTag))]
partial struct ResourceStackingJob : IJobEntity
{
    [ReadOnly] public Grid grid;
    [ReadOnly] public ResourceConfiguration config;
    [NativeDisableParallelForRestriction] public NativeArray<int> heightData;

    [BurstCompile]
    void Execute(ref TransformAspect transform, ref Stacked stacked)
    {
        var stackHeightMap = new NativeArray2DProxy<int>
        {
            shape = math.int2(grid.Shape.x, grid.Shape.z),
            data = heightData
        };
        var position = transform.Position;
        var idx = grid.PositionToIndex(position);
        stacked.Index =
            Interlocked.Increment(ref stackHeightMap.data.AsSpan()[idx.x * stackHeightMap.shape[1] + idx.z]);
        position.y = stacked.Index * config.resourceSize + grid.bottom;
        transform.Position = position;
    }
}

[UpdateInGroup(typeof(ResourceSimulationGroup))]
[UpdateAfter(typeof(ResourceFallenSystem))]
[BurstCompile]
partial struct ResourceStackingSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Grid>();
        state.RequireForUpdate<StackHeight>();
        state.RequireForUpdate<ResourceConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var grid = SystemAPI.GetSingleton<Grid>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var stackHeight = SystemAPI.GetSingletonBuffer<StackHeight>().Reinterpret<int>().AsNativeArray();
        stackHeight.AsSpan().Clear();

        state.Dependency = new ResourceStackingJob
        {
            grid = grid,
            heightData = stackHeight,
            config = config
        }.ScheduleParallel(state.Dependency);
    }
}

[WithAll(typeof(ResourceTag))]
partial struct ResourceRemoveOverHeightJob : IJobEntity
{
    [ReadOnly] public ResourceConfiguration config;
    [ReadOnly] public FieldComponent field;
    public EntityCommandBuffer.ParallelWriter ECB;

    void Execute(in Stacked stacked, in Entity entity, [EntityInQueryIndex] int inQueryIndex)
    {
        if (stacked.Index * config.resourceSize >= field.Size.y)
        {
            ECB.DestroyEntity(inQueryIndex, entity);
        }
    }
}

[UpdateInGroup(typeof(ResourceSimulationGroup), OrderLast = true)]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct ResourceOverHeightRemoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResourceConfiguration>();
        state.RequireForUpdate<FieldComponent>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var field = SystemAPI.GetSingleton<FieldComponent>();


        // if (resource.holder == null && resource.stacked == false)
        state.Dependency =
            new ResourceRemoveOverHeightJob { config = config, field = field, ECB = ecb }.ScheduleParallel(
                state.Dependency);
    }
}