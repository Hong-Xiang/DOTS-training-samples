using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using Unity.Transforms;
using static UnityEditor.Rendering.CameraUI;
using System.Linq;

public struct Obstacle : IComponentData
{
    public float2 position;
    public float radius;
}

[BurstCompile]
partial struct StaticObjectUtils
{
    [BurstCompile]
    public Colony CreateColony(ref EntityCommandBuffer cmd, in Entity prefab)
    {
        return new Colony
        {
            instance = cmd.Instantiate(prefab)
        };
    }

    [BurstCompile]
    public Resource CreateResource(ref EntityCommandBuffer cmd, in Entity prefab)
    {
        return new Resource
        {
            instance = cmd.Instantiate(prefab)
        };
    }
}

[BurstCompile]
partial struct Colony
{
    public Entity instance;

    [BurstCompile]
    public void SetLocation(ref EntityCommandBuffer cmd, float2 position)
    {
        cmd.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(
                        position.x,
                        position.y,
                        0f
                    )
        });
    }
}

[BurstCompile]
partial struct Resource
{
    public Entity instance;

    [BurstCompile]
    public void SetLocation(ref EntityCommandBuffer cmd, int mapSize)
    {
        float resourceAngle = UnityEngine.Random.value * 2f * Mathf.PI;
        var position = Vector2.one * mapSize * .5f + new Vector2(Mathf.Cos(resourceAngle) * mapSize * .475f, Mathf.Sin(resourceAngle) * mapSize * .475f);

        cmd.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(
                        position.x,
                        position.y,
                        0f
                    )
        });
    }
}

// Unmanaged systems based on ISystem can be Burst compiled, but this is not yet the default.
// So we have to explicitly opt into Burst compilation with the [BurstCompile] attribute.
// It has to be added on BOTH the struct AND the OnCreate/OnDestroy/OnUpdate functions to be
// effective.
[BurstCompile]
partial struct StaticObjectsGenerateSystem : ISystem
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

    [BurstCompile]
    public void GenerateObstacles(ref EntityCommandBuffer cmd, ConfigurationComponent config)
    {
        for (var i = 1; i <= config.obstacleRingCount; i++)
        {
            float ringRadius = (i / (config.obstacleRingCount + 1f)) * (config.mapSize * .5f);
            float circumference = ringRadius * 2f * Mathf.PI;
            int maxCount = Mathf.CeilToInt(circumference / (2f * config.obstacleRadius) * 2f);
            int offset = UnityEngine.Random.Range(0, maxCount);
            int holeCount = UnityEngine.Random.Range(1, 3);
            for (int j = 0; j < maxCount; j++)
            {
                float t = (float)j / maxCount;
                if ((t * holeCount) % 1f < config.obstaclesPerRing)
                {
                    float angle = (j + offset) / (float)maxCount * (2f * Mathf.PI);
                    Obstacle obstacle = new Obstacle();
                    obstacle.position = new float2(config.mapSize * .5f + Mathf.Cos(angle) * ringRadius,
                        config.mapSize * .5f + Mathf.Sin(angle) * ringRadius);
                    obstacle.radius = config.obstacleRadius;

                    var instance = cmd.Instantiate(config.ObstaclePrefab);
                    cmd.AddComponent(instance, obstacle);
                    cmd.SetComponent(instance, new LocalToWorldTransform
                    {
                        Value = UniformScaleTransform.FromPosition(
                            obstacle.position.x,
                            obstacle.position.y,
                            0.0f
                        )
                    });
                    cmd.AddComponent(instance, new Obstacle { radius = config.obstacleRadius });
                }
            }
        }
    }

    // See note above regarding the [BurstCompile] attribute.
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        ConfigurationComponent c = new ConfigurationComponent();
        foreach (var c_ in SystemAPI.Query<ConfigurationComponent>())
        {
            c = c_;
        }
        GenerateObstacles(ref ecb, c);

        var util = new StaticObjectUtils();

        var colony = util.CreateColony(ref ecb, c.ColonyPrefab);
        colony.SetLocation(ref ecb, Vector2.one * c.mapSize * .5f);

        //float resourceAngle = Random.value * 2f * Mathf.PI;
        //resourcePosition = Vector2.one * mapSize * .5f + new Vector2(Mathf.Cos(resourceAngle) * mapSize * .475f, Mathf.Sin(resourceAngle) * mapSize * .475f);

        var resource = util.CreateResource(ref ecb, c.ResourcePrefab);
        resource.SetLocation(ref ecb, c.mapSize);


        state.Enabled = false;
    }
}



