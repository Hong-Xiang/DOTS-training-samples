using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using Unity.Transforms;





struct Resource : IComponentData
{
}

struct Colony : IComponentData
{
}


[BurstCompile]
partial struct MapBoundary
{
    public float2 X;
    public float2 Y;
    public (float2, float2) BoundaryCollision(float2 velocity, in float2 previousPosition, float2 updatedPosition)
    {
        if (updatedPosition.x < X[0] || updatedPosition.x > X[1])
        {
            updatedPosition.x = previousPosition.x;
            velocity.x = -velocity.x;
        }
        if (updatedPosition.y < Y[0] || updatedPosition.y > Y[1])
        {
            updatedPosition.y = previousPosition.y;
            velocity.y = -velocity.y;
        }
        return (updatedPosition, velocity);
    }
}


[BurstCompile]
partial struct MapGenerationSystem : ISystem
{
    // Every function defined by ISystem has to be implemented even if empty.
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    // Every function defined by ISystem has to be implemented even if empty.
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }


    // See note above regarding the [BurstCompile] attribute.
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var c in SystemAPI.Query<ConfigurationComponent>())
        {
            var colony = ecb.Instantiate(c.ColonyPrefab);
            ecb.SetComponent(colony, new LocalToWorldTransform
            {
                Value = UniformScaleTransform.FromPosition(math.float3(math.float2(c.mapSize * .5f), 0f))
            });
            ecb.AddComponent(colony, new Colony());

            var resource = ecb.Instantiate(c.ResourcePrefab);
            float resourceAngle = UnityEngine.Random.value * 2f * Mathf.PI;
            var resourcePosition =
               math.float2(c.mapSize * .5f) + math.float2(math.cos(resourceAngle), math.sin(resourceAngle)) * c.mapSize * .475f;
            ecb.SetComponent(resource, new LocalToWorldTransform
            {
                Value = UniformScaleTransform.FromPosition(math.float3(resourcePosition, 0f))
            });
            ecb.AddComponent(resource, new Resource());


        }
        state.Enabled = false;
    }
}



