using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Collections;

partial struct ResourceConfiguration : IComponentData
{
    public Entity resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate;
    public int beesPerResource;
    public int startResourceCount;
}

struct HittingSupport : IComponentData
{
}

struct StackIndex : IComponentData
{
    public int Value;
}

partial struct Resource : IComponentData
{
    public float3 Velocity;
    public bool HasHolder;
    public bool Stacked => false;
    public bool isHolderDead => true;
}

struct ResourceGrid
{
    public int3 Shape;
    public float3 Size;
    public float3 Center;

    public float3 IndexToPosition(int3 idx)
    {
        return Size / math.float3(Shape) * (math.float3(idx) + .5f) + Center - Size * .5f;
    }

    public int3 PositinToIndex(float3 pos)
    {
        return math.int3((pos - Center + Size * .5f) / (Size / Shape));
    }

}

partial struct ResourceSpawnSystem : ISystem
{

    public NativeArray<int> gridStack;
    int2 gridShape;

    public Unity.Mathematics.Random random;
    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(42);
        gridShape = math.int2(512, 512);
        gridStack = new NativeArray<int>(gridShape.x * gridShape.y, Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        gridStack.Dispose();
    }
    bool ShouldSpawn()
    {
        // return resourcesCount < 1000 && MouseRaycaster.isMouseTouchingField && Input.GetKey(KeyCode.Mouse0);
        return Input.GetKey(KeyCode.Mouse0);
    }



    void SpawnOnMouseClick(ResourceConfiguration config)
    {
        if (ShouldSpawn())
        {
            // spawnTimer += Time.deltaTime;
            // while (spawnTimer > 1f / spawnRate)
            // {
            //     spawnTimer -= 1f / spawnRate;
            //     SpawnResource(MouseRaycaster.worldMousePosition);
            // }
        }
    }


    void SpawnResource(
         ref EntityCommandBuffer ECB,
         Entity resourcePrefab,
         float scale,
         float3 position)
    {
        var instance = ECB.Instantiate(resourcePrefab);
        ECB.AddComponent(instance, new Resource
        {
            Velocity = float3.zero,
            HasHolder = false
        });
        ECB.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position).ApplyScale(scale)
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        var field = SystemAPI.GetSingleton<FieldConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var config = SystemAPI.GetSingleton<ResourceConfiguration>();

        var grid = new ResourceGrid
        {
            Shape = math.int3(math.ceil(field.Size / config.resourceSize)),
            Size = field.Size,
            Center = math.float3(0f, field.Size.y * .5f, 0f)
        };


        if (ShouldSpawn())
        {
            var position = random.NextFloat3(grid.Center - grid.Size * .5f, grid.Center + grid.Size * .5f);
            position.y = 0;
            var idx = grid.PositinToIndex(position);
            // position.y = gridStack[idx.x * gridShape.y + idx.y] * config.resourceSize;
            gridStack[idx.x * gridShape.y + idx.y] += 1;
            position = grid.IndexToPosition(math.int3(idx.x, gridStack[idx.x * gridShape.y + idx.y], idx.z));
            SpawnResource(ref ecb, config.resourcePrefab, config.resourceSize, position);
            Debug.Log(grid.IndexToPosition(math.int3(0, 1, 0)));
            Debug.Log(grid.Center);
            Debug.Log(grid.Size);
            Debug.Log(grid.Shape);
            Debug.Log(config.resourceSize);
        }
    }
}

partial struct ResourceAccelerationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var fieldConfig = SystemAPI.GetSingleton<FieldConfiguration>();

        foreach (var v in SystemAPI.Query<RefRW<Resource>>())
        {
            v.ValueRW.Velocity += fieldConfig.Gravity * math.float3(0f, 1f, 0f);
        }
    }
}



partial struct ResourceMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    // int2 GetGridIndex(float3 pos)
    // {
    //     int2 idx = math.floor((pos - minGridPos + gridSize * .5f) / gridSize);
    //     gridX = Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
    //     gridY = Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);


    //     gridX = Mathf.Clamp(gridX, 0, gridCounts.x - 1);
    //     gridY = Mathf.Clamp(gridY, 0, gridCounts.y - 1);
    // }


    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (v, t, e) in SystemAPI.Query<Resource, TransformAspect>().WithEntityAccess())
        {
            t.Position += v.Velocity * SystemAPI.Time.DeltaTime;
            // var gridIdx = GetGridIndex(t.Position);
            // float floorY = GetStackPos(resource.gridX, resource.gridY, stackHeights[resource.gridX, resource.gridY]).y;
            if (t.Position.y <= 0f)
            {
                ecb.AddComponent(e, new HittingSupport());
                t.Position = math.float3(t.Position.x, 0f, t.Position.z);
            }
        }
    }
}

partial class ResourceManagerSystem : SystemBase
{
    int[,] stackHeight;

    void MoveResourceToFollowHolder(Resource resource, float3 holderPosition, ref float3 resourcePosition, ref float3 resourceVelocity)
    {
        if (resource.isHolderDead)
        {
            resource.HasHolder = false;
        }
        else
        {
            // Vector3 targetPos = resource.holder.position - Vector3.up * (resourceSize + resource.holder.size) * .5f;
            // resourcePosition = Vector3.Lerp(resource.position, targetPos, carryStiffness * Time.deltaTime);
            // resourceVelocity = resource.holder.velocity;
        }
    }

    void DrawResources()
    {
        // Vector3 scale = new Vector3(resourceSize, resourceSize * .5f, resourceSize);
        // for (int i = 0; i < resources.Count; i++)
        // {
        //     matrices[i] = Matrix4x4.TRS(resources[i].position, Quaternion.identity, scale);
        // }
        // Graphics.DrawMeshInstanced(resourceMesh, 0, resourceMaterial, matrices);
    }

    void LimitResourcePositionByFieldSize()
    {
        for (int j = 0; j < 3; j++)
        {
            // if (System.Math.Abs(resource.position[j]) > Field.size[j] * .5f)
            // {
            //     resource.position[j] = Field.size[j] * .5f * Mathf.Sign(resource.position[j]);
            //     resource.velocity[j] *= -.5f;
            //     resource.velocity[(j + 1) % 3] *= .8f;
            //     resource.velocity[(j + 2) % 3] *= .8f;
            // }
        }
    }



    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<ResourceConfiguration>();

        using var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (resource, transform, entity) in SystemAPI.Query<Resource, TransformAspect>().WithEntityAccess())
        {
            var resourcePosition = transform.Position;

            if (resource.HasHolder)
            {
                // TODO: implement
                // MoveResourceToFollowHolder(resource);
            }
            else if (!resource.Stacked)
            {
                // resource.position = Vector3.Lerp(resource.position, NearestSnappedPos(resource.position), snapStiffness * Time.deltaTime);
                // resource.velocity.y += Field.gravity * Time.deltaTime;
                // resource.position += resource.velocity * Time.deltaTime;

                // GetGridIndex(resource.position, out resource.gridX, out resource.gridY);

                // float floorY = GetStackPos(resource.gridX, resource.gridY, stackHeights[resource.gridX, resource.gridY]).y;

                // LimitResourcePositionByFieldSize();

                // if (resourcePosition.y < floorY)
                // {
                //     ecb.AddComponent(entity, new HittingGround());
                // }
            }
        }
    }
}

partial struct ResourceHittingSupportSystem : ISystem
{
    void ResourceHitGround(Resource resource)
    {
        // resource.position.y = floorY;
        // if (Mathf.Abs(resource.position.x) > Field.size.x * .4f)
        // {
        //     int team = 0;
        //     if (resource.position.x > 0f)
        //     {
        //         team = 1;
        //     }
        //     for (int j = 0; j < beesPerResource; j++)
        //     {
        //         BeeManager.SpawnBee(resource.position, team);
        //     }
        //     ParticleManager.SpawnParticle(resource.position, ParticleType.SpawnFlash, Vector3.zero, 6f, 5);
        //     DeleteResource(resource);
        // }
        // else
        // {
        //     resource.stacked = true;
        //     resource.stackIndex = stackHeights[resource.gridX, resource.gridY];
        //     if ((resource.stackIndex + 1) * resourceSize < Field.size.y)
        //     {
        //         stackHeights[resource.gridX, resource.gridY]++;
        //     }
        //     else
        //     {
        //         DeleteResource(resource);
        //     }
        // }
    }
    public void OnCreate(ref SystemState state)
    {

    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {


        foreach (var resource in SystemAPI.Query<Resource>().WithAll<HittingSupport>())
        {
            ResourceHitGround(resource);
        }

    }
}