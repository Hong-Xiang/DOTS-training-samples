using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[RequireMatchingQueriesForUpdate]
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
    void SpawnResource(ref EntityCommandBuffer ECB, float3 position, float scale, Entity prefab)
    {
        var instance = ECB.Instantiate(prefab);
        ECB.AddComponent(instance, new ResourceComponent { });
        ECB.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position).ApplyScale(scale)
        });
        ECB.AddComponent(instance, new Velocity { Value = float3.zero });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
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
        var resourcesCount = SystemAPI.QueryBuilder().WithAll<ResourceComponent>().Build().CalculateEntityCount();

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

[WithAll(typeof(ResourceComponent))]
[BurstCompile]
partial struct ResourceFollowHolderJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<BeeSize> BeeSizeFromEntity;
    [NativeDisableParallelForRestriction]
    public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    [ReadOnly] public ComponentLookup<Velocity> VelocityFromEntity;
    public EntityCommandBuffer ECB;
    public ResourceConfiguration config;
    [BurstCompile]
    public void Execute(ref Velocity velocity,
                        in TransformAspect transform,
                        in ResourceHolder holder,
                        in Entity e)
    {
        if ((!SystemAPI.Exists(holder.Holder)) || SystemAPI.HasComponent<Dying>(holder.Holder))
        {
            ECB.RemoveComponent<ResourceHolder>(e);
        }
        else
        {
            var holderSize = BeeSizeFromEntity[holder.Holder].size;
            var holderPosition = LocalToWorldTransformFromEntity[holder.Holder].Value.Position;
            float3 targetPos = holderPosition - math.float3(Vector3.up) * (config.resourceSize + holderSize) * .5f;
            transform.Position = math.lerp(transform.Position, targetPos, config.carryStiffness * SystemAPI.Time.DeltaTime);
            velocity.Value = VelocityFromEntity[holder.Holder].Value;
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
    ComponentLookup<Velocity> VelocityFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BeeSizeFromEntity = state.GetComponentLookup<BeeSize>(true);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);

        state.RequireForUpdate<ResourceConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        BeeSizeFromEntity.Update(ref state);
        LocalToWorldTransformFromEntity.Update(ref state);
        VelocityFromEntity.Update(ref state);


        foreach (var (resource, holder, transform, velocity, e) in SystemAPI.Query<ResourceComponent, ResourceHolder, TransformAspect, RefRW<Velocity>>().WithEntityAccess())
        {

            if ((!SystemAPI.Exists(holder.Holder)) || SystemAPI.HasComponent<Dying>(holder.Holder))
            {
                ecb.RemoveComponent<ResourceHolder>(e);
            }
            else
            {
                var bee = BeeSizeFromEntity[holder.Holder];
                var holderPosition = LocalToWorldTransformFromEntity[holder.Holder].Value.Position;
                float3 targetPos = holderPosition - math.float3(Vector3.up) * (config.resourceSize + bee.size) * .5f;
                transform.Position = math.lerp(transform.Position, targetPos, config.carryStiffness * SystemAPI.Time.DeltaTime);
                velocity.ValueRW.Value = VelocityFromEntity[holder.Holder].Value;
            }
        }
        // new ResourceFollowHolderJob
        // {
        //     BeeFromEntity = BeeFromEntity,
        //     LocalToWorldTransformFromEntity = LocalToWorldTransformFromEntity,
        //     VelocityFromEntity = VelocityFromEntity,
        //     ECB = ecb
        // }.Schedule();
    }
}


struct ResourceStackHoldLogic
{
    public ComponentLookup<ResourceHolder> ResourceHolderFromEntity;

    void OnUpdate(ref SystemState state)
    {
        ResourceHolderFromEntity.Update(ref state);
    }

    public bool HasHolder(in Entity resourceEntity)
    {
        return ResourceHolderFromEntity.HasComponent(resourceEntity);
    }
    public bool IsGrabable(in Entity resourceEntity)
    {
        return false;
    }
}



[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
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
    }

    public void OnDestroy(ref SystemState state)
    {
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var grid = SystemAPI.GetSingleton<Grid>();
        var stackHeight = new NativeArray2DProxy<int>
        {
            shape = math.int2(grid.Shape.x, grid.Shape.z),
            data = SystemAPI.GetSingletonBuffer<StackHeight>().Reinterpret<int>().AsNativeArray()
        };

        var beeConfig = SystemAPI.GetSingleton<BeeConfiguration>();
        var field = SystemAPI.GetSingleton<FieldComponent>();


        var particleSpawner = new ParticleSpawner
        {
            config = SystemAPI.GetSingleton<ParticleConfiguration>()
        };


        // if (resource.holder == null && resource.stacked == false)
        foreach (var (resource, transform, velocity, e) in SystemAPI.Query<
            ResourceComponent,
            TransformAspect,
            RefRW<Velocity>>().WithNone<ResourceHolder, Stacked>().WithEntityAccess())
        {
            var position = transform.Position;

            position = math.lerp(position, grid.NearestSnappedPos(position), config.snapStiffness * SystemAPI.Time.DeltaTime);

            var v = velocity.ValueRO.Value;
            v.y += field.Gravity * SystemAPI.Time.DeltaTime;
            position += v * SystemAPI.Time.DeltaTime;


            var idx = grid.ToInboundIndex(grid.PositionToIndex(position));

            // 当resource比较多的时候，可能同时存在多个resource在同一个stack上，此时下面的floorY判断逻辑会出问题
            float floorY = grid.bottom + stackHeight[idx.x, idx.z] * config.resourceSize;
            for (int j = 0; j < 3; j++)
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
                        particleSpawner.SpawnParticleSpawnFlash(ref random, ecb, position + random.NextFloat3Direction(), random.NextFloat3Direction());
                    }
                    for (int i = 0; i < config.beesPerResource; i++)
                    {
                        BeeSpawnSystem.SpawnBee(ref ecb, ref random, position, position.x < 0 ? 0 : 1, beeConfig);
                    }
                    ecb.DestroyEntity(e);
                    // Resource Spawn Bee System
                }
                else
                {
                    // resource.stacked = true;
                    ecb.AddComponent(e, new Stacked
                    {
                        Index = stackHeight[idx.x, idx.z]
                    });
                    stackHeight[idx.x, idx.z]++;
                    // DeleteResource(resource);
                }
            }
            transform.Position = position;
            velocity.ValueRW.Value = v;
        }
    }
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
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
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var field = SystemAPI.GetSingleton<FieldComponent>();
        // if (resource.holder == null && resource.stacked == false)
        foreach (var (resource, stacked, e) in SystemAPI.Query<
            ResourceComponent,
            Stacked>().WithEntityAccess())
        {
            if (stacked.Index * config.resourceSize >= field.Size.y)
            {
                ecb.DestroyEntity(e);
            }
        }
    }
}
