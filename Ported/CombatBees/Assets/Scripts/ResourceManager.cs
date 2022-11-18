using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

public class ResourceManager : MonoBehaviour
{
    public GameObject resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate = .1f;
    public int beesPerResource;
    [Space(10)]
    public int startResourceCount;

    class ResourceManagerBaker : Baker<ResourceManager>
    {
        public override void Bake(ResourceManager authoring)
        {
            AddComponent(new ResourceConfiguration
            {
                resourcePrefab = GetEntity(authoring.resourcePrefab),
                resourceSize = authoring.resourceSize,
                snapStiffness = authoring.snapStiffness,
                carryStiffness = authoring.carryStiffness,
                spawnRate = authoring.spawnRate,
                beesPerResource = authoring.beesPerResource,
                startResourceCount = authoring.startResourceCount
            });
        }
    }
}

struct ResourceConfiguration : IComponentData
{
    public Entity resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate;
    public int beesPerResource;
    public int startResourceCount;

}

partial struct Native2DArray<T> where T : struct
{
    int2 shape;
    NativeArray<T> data;
}

partial class ResourceSystem : SystemBase
{
    public ResourceConfiguration config;
    Unity.Mathematics.Random random;


    List<Resource> resources;
    Vector2Int gridCounts;
    Vector2 gridSize;
    Vector2 minGridPos;

    bool isFirstRun = true;



    int[,] stackHeights;

    float spawnTimer = 0f;

    public static ResourceSystem instance;

    public static Resource TryGetRandomResource()
    {
        if (instance.resources.Count == 0)
        {
            return null;
        }
        else
        {
            Resource resource = instance.resources[UnityEngine.Random.Range(0, instance.resources.Count)];
            int stackHeight = instance.stackHeights[resource.gridX, resource.gridY];
            if (resource.holder == null || resource.stackIndex == stackHeight - 1)
            {
                return resource;
            }
            else
            {
                return null;
            }
        }
    }

    public static bool IsTopOfStack(Resource resource)
    {
        int stackHeight = instance.stackHeights[resource.gridX, resource.gridY];
        return resource.stackIndex == stackHeight - 1;
    }

    Vector3 GetStackPos(int x, int y, int height)
    {
        return new Vector3(minGridPos.x + x * gridSize.x, -Field.size.y * .5f + (height + .5f) * config.resourceSize, minGridPos.y + y * gridSize.y);
    }

    Vector3 NearestSnappedPos(Vector3 pos)
    {
        int x, y;
        GetGridIndex(pos, out x, out y);
        return new Vector3(minGridPos.x + x * gridSize.x, pos.y, minGridPos.y + y * gridSize.y);
    }
    void GetGridIndex(Vector3 pos, out int gridX, out int gridY)
    {
        gridX = Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
        gridY = Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);

        gridX = Mathf.Clamp(gridX, 0, gridCounts.x - 1);
        gridY = Mathf.Clamp(gridY, 0, gridCounts.y - 1);
    }

    void SpawnResource(ref EntityCommandBuffer ECB)
    {
        Vector3 pos = new Vector3(minGridPos.x * .25f + random.NextFloat() * Field.size.x * .25f, random.NextFloat() * 10f, minGridPos.y + random.NextFloat() * Field.size.z);
        SpawnResource(ref ECB, pos);
    }
    void SpawnResource(ref EntityCommandBuffer ECB, Vector3 pos)
    {
        Resource resource = new Resource(pos);
        var instance = EntityManager.Instantiate(config.resourcePrefab);
        // var instance = ECB.Instantiate(config.resourcePrefab);
        EntityManager.SetComponentData(instance, new LocalToParentTransform
        {
            Value = UniformScaleTransform.FromPosition(pos)
        });
        resources.Add(resource);
    }
    void DeleteResource(Resource resource)
    {
        resource.dead = true;
        resources.Remove(resource);
    }

    public static void GrabResource(Bee bee, Resource resource)
    {
        resource.holder = bee;
        resource.stacked = false;
        instance.stackHeights[resource.gridX, resource.gridY]--;
    }


    protected override void OnCreate()
    {

        instance = this;
        random = new Unity.Mathematics.Random(42);
    }

    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        // var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        // var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        if (isFirstRun)
        {
            config = SystemAPI.GetSingleton<ResourceConfiguration>();

            resources = new List<Resource>();

            gridCounts = Vector2Int.RoundToInt(new Vector2(Field.size.x, Field.size.z) / config.resourceSize);
            gridSize = new Vector2(Field.size.x / gridCounts.x, Field.size.z / gridCounts.y);
            minGridPos = new Vector2((gridCounts.x - 1f) * -.5f * gridSize.x, (gridCounts.y - 1f) * -.5f * gridSize.y);
            stackHeights = new int[gridCounts.x, gridCounts.y];

            for (int i = 0; i < config.startResourceCount; i++)
            {
                SpawnResource(ref ecb);
            }
            isFirstRun = false;
        }



        if (resources.Count < 1000 && MouseRaycaster.isMouseTouchingField)
        {
            if (Input.GetKey(KeyCode.Mouse0))
            {
                spawnTimer += SystemAPI.Time.DeltaTime;
                while (spawnTimer > 1f / config.spawnRate)
                {
                    spawnTimer -= 1f / config.spawnRate;
                    SpawnResource(ref ecb, MouseRaycaster.worldMousePosition);
                }
            }
        }
        SpawnResource(ref ecb, Vector3.zero);
        ecb.Playback(EntityManager);

        for (int i = 0; i < resources.Count; i++)
        {
            Resource resource = resources[i];
            if (resource.holder != null)
            {
                if (resource.holder.dead)
                {
                    resource.holder = null;
                }
                else
                {
                    Vector3 targetPos = resource.holder.position - Vector3.up * (config.resourceSize + resource.holder.size) * .5f;
                    resource.position = Vector3.Lerp(resource.position, targetPos, config.carryStiffness * SystemAPI.Time.DeltaTime);
                    resource.velocity = resource.holder.velocity;
                }
            }
            else if (resource.stacked == false)
            {
                resource.position = Vector3.Lerp(resource.position, NearestSnappedPos(resource.position), config.snapStiffness * SystemAPI.Time.DeltaTime);
                resource.velocity.y += Field.gravity * SystemAPI.Time.DeltaTime;
                resource.position += resource.velocity * SystemAPI.Time.DeltaTime;
                GetGridIndex(resource.position, out resource.gridX, out resource.gridY);
                float floorY = GetStackPos(resource.gridX, resource.gridY, stackHeights[resource.gridX, resource.gridY]).y;
                for (int j = 0; j < 3; j++)
                {
                    if (System.Math.Abs(resource.position[j]) > Field.size[j] * .5f)
                    {
                        resource.position[j] = Field.size[j] * .5f * Mathf.Sign(resource.position[j]);
                        resource.velocity[j] *= -.5f;
                        resource.velocity[(j + 1) % 3] *= .8f;
                        resource.velocity[(j + 2) % 3] *= .8f;
                    }
                }
                if (resource.position.y < floorY)
                {
                    resource.position.y = floorY;
                    if (Mathf.Abs(resource.position.x) > Field.size.x * .4f)
                    {
                        int team = 0;
                        if (resource.position.x > 0f)
                        {
                            team = 1;
                        }
                        for (int j = 0; j < config.beesPerResource; j++)
                        {
                            BeeManager.SpawnBee(resource.position, team);
                        }
                        ParticleManager.SpawnParticle(resource.position, ParticleType.SpawnFlash, Vector3.zero, 6f, 5);
                        DeleteResource(resource);
                    }
                    else
                    {
                        resource.stacked = true;
                        resource.stackIndex = stackHeights[resource.gridX, resource.gridY];
                        if ((resource.stackIndex + 1) * config.resourceSize < Field.size.y)
                        {
                            stackHeights[resource.gridX, resource.gridY]++;
                        }
                        else
                        {
                            DeleteResource(resource);
                        }

                    }
                }
            }
        }

        ecb.Dispose();


    }
}
