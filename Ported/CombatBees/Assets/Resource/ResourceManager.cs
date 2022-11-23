using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using System;
using Unity.Burst;

struct ResourceConfiguration : IComponentData
{
    public Entity resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate;
    public int beesPerResource;
    public int startResourceCount;
    public int maxResourceCount;
}



partial struct ResourceSpawnSystem : ISystem
{
    bool isFirstFrame;
    Unity.Mathematics.Random random;

    public void OnCreate(ref SystemState state)
    {
        isFirstFrame = true;
        random = Unity.Mathematics.Random.CreateFromIndex(42);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

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

    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
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

partial struct ResourceSpawnBeeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        // int team = 0;
        // if (resource.position.x > 0f)
        // {
        //     team = 1;
        // }
        // for (int j = 0; j < config.beesPerResource; j++)
        // {
        //     // TODO: add method to span bee
        //     // BeeManagerSystem.SpawnBee(resource.position, team);
        // }
        // ParticleManager.SpawnParticle(resource.position, ParticleType.SpawnFlash, Vector3.zero, 6f, 5);
        // DeleteResource(resource);
    }
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
partial struct ResourceFollowHolderSystem : ISystem
{
    ComponentLookup<BeeComponent> BeeFromEntity;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Velocity> VelocityFromEntity;
    public void OnCreate(ref SystemState state)
    {
        BeeFromEntity = state.GetComponentLookup<BeeComponent>(true);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        BeeFromEntity.Update(ref state);
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
                var bee = BeeFromEntity[holder.Holder];
                var holderPosition = LocalToWorldTransformFromEntity[holder.Holder].Value.Position;
                float3 targetPos = holderPosition - math.float3(Vector3.up) * (config.resourceSize + bee.size) * .5f;
                transform.Position = math.lerp(transform.Position, targetPos, config.carryStiffness * SystemAPI.Time.DeltaTime);
                velocity.ValueRW.Value = VelocityFromEntity[holder.Holder].Value;
            }
        }
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
partial struct ResourceFallenSystem : ISystem
{
    Unity.Mathematics.Random random;
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(42);
    }

    public void OnDestroy(ref SystemState state)
    {
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
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
partial struct ResourceOverHeightRemoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        // if (resource.holder == null && resource.stacked == false)
        foreach (var (resource, stacked, e) in SystemAPI.Query<
            ResourceComponent,
            Stacked>().WithEntityAccess())
        {
            if (stacked.Index * config.resourceSize >= Field.size.y)
            {
                ecb.DestroyEntity(e);
            }
        }
    }
}

partial struct ResourceSystem : ISystem
{
    public ResourceConfiguration config;
    public EntityQuery beeQuery;

    Unity.Mathematics.Random random;


    Vector2Int gridCounts;
    Vector2 gridSize;
    Vector2 minGridPos;

    bool isFirstRun;




    float spawnTimer;

    public static ResourceSystem instance;

    // public static Resource TryGetRandomResource()
    // {
    //     if (instance.resources.Count == 0)
    //     {
    //         return null;
    //     }
    //     else
    //     {
    //         Resource resource = instance.resources[UnityEngine.Random.Range(0, instance.resources.Count)];
    //         int stackHeight = instance.stackHeights[resource.gridX, resource.gridY];
    //         if (resource.holder == null || resource.stackIndex == stackHeight - 1)
    //         {
    //             return resource;
    //         }
    //         else
    //         {
    //             return null;
    //         }

    //     }
    // }

    public static bool IsTopOfStack(Resource resource)
    {
        return true;
        // int stackHeight = instance.stackHeights[resource.gridX, resource.gridY];
        // return resource.stackIndex == stackHeight - 1;
    }

    Vector3 GetStackPos(int x, int y, int height)
    {
        return new Vector3(minGridPos.x + x * gridSize.x,
                           -Field.size.y * .5f + (height + .5f) * config.resourceSize,
                           minGridPos.y + y * gridSize.y);
    }


    void GetGridIndex(Vector3 pos, out int gridX, out int gridY)
    {
        gridX = Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
        gridY = Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);

        gridX = Mathf.Clamp(gridX, 0, gridCounts.x - 1);
        gridY = Mathf.Clamp(gridY, 0, gridCounts.y - 1);
    }


    void DeleteResource(Resource resource)
    {
        resource.dead = true;
        // resources.Remove(resource);
    }

    public static void GrabResource(Bee bee, Resource resource)
    {
        resource.holder = bee;
        resource.stacked = false;
        // instance.stackHeights[resource.gridX, resource.gridY]--;
    }


    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
        instance = this;
        random = new Unity.Mathematics.Random(42);
        isFirstRun = true;
        spawnTimer = 0f;
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        foreach (var resource in SystemAPI.Query<ResourceComponent>())
        {





        }



    }

    public void OnDestroy(ref SystemState state)
    {
    }
}
