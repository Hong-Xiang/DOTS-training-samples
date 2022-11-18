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

struct HittingGround : IComponentData
{
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
    public int2 Shape;
    public float2 Size;

    float2 IndexToPosition(int2 idx)
    {
        return Size / math.float2(Shape) * (math.float2(idx) + .5f) - Size * .5f;
    }

}

partial struct ResourceSpawnSystem : ISystem
{


    public Unity.Mathematics.Random random;
    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(42);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    void SpawnResource(
         ref EntityCommandBuffer ECB,
         Entity resourcePrefab,
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
            Value = UniformScaleTransform.FromPosition(position)
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        var field = SystemAPI.GetSingleton<FieldConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var config = SystemAPI.GetSingleton<ResourceConfiguration>();

        var gridSize = math.float2(field.Size.xz);
        var grid = new ResourceGrid
        {
            Shape = math.int2(math.ceil(gridSize / config.resourceSize)),
            Size = gridSize
        };




        var randomSize = 1f;

        // var position = math.float3(
        //     UnityEngine.Random.Range(-randomSize, randomSize),
        //     UnityEngine.Random.Range(-randomSize, randomSize),
        //     UnityEngine.Random.Range(-randomSize, randomSize)
        // );
        // var position = new Vector3(minGridPos.x * .25f + Random.value * Field.size.x * .25f, Random.value * 10f, minGridPos.y + Random.value * Field.size.z);
        var position = random.NextFloat3(
            math.float3(0f),
            field.Size
        ) - math.float3(field.Size.x * .5f, 0f, field.Size.z * .5f);
        SpawnResource(ref ecb, config.resourcePrefab, position);
    }
}

partial class ResourceManagerSystem : SystemBase
{
    int[,] stackHeight;

    bool ShouldSpawnResource()
    {
        // return resourcesCount < 1000 && MouseRaycaster.isMouseTouchingField && Input.GetKey(KeyCode.Mouse0);
        return false;
    }



    void SpawnOnMouseClick(ResourceConfiguration config)
    {
        if (ShouldSpawnResource())
        {
            // spawnTimer += Time.deltaTime;
            // while (spawnTimer > 1f / spawnRate)
            // {
            //     spawnTimer -= 1f / spawnRate;
            //     SpawnResource(MouseRaycaster.worldMousePosition);
            // }
        }
    }

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

partial struct ResourceHittingGroundSystem : ISystem
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


        foreach (var resource in SystemAPI.Query<Resource>().WithAll<HittingGround>())
        {
            ResourceHitGround(resource);
        }

    }
}