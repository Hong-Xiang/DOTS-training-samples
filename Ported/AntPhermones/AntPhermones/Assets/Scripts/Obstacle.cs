using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using Unity.Transforms;

[BurstCompile]
partial struct Obstacle : IComponentData
{
    public float radius;

    [BurstCompile]
    public (float2, float2) BoundaryCollision(float2 velocity, in float2 obstaclePosition, in float2 targetPosition)
    {
        var delta = targetPosition - obstaclePosition;
        var sqrDist = math.lengthsq(delta);
        if (sqrDist < radius * radius)
        {
            var n = math.normalize(delta);
            // Question: why not using reflect ?
            // velocity = math.reflect(velocity, delta);
            velocity -= n * math.dot(n, velocity) * 1.5f;
            return (obstaclePosition + n * radius, velocity);

        }
        else
        {
            return (targetPosition, velocity);
        }
    }

    [BurstCompile]
    public bool LineIntersect(in float2 s, in float2 t, in float2 obstaclePosition, float obstacleRadius)
    {
        var d = t - s;
        var l = math.length(d);
        int stepCount = (int)math.ceil(l / .5f);
        var obstacleRadiusSq = obstacleRadius * obstacleRadius;
        for (int i = 0; i < stepCount; i++)
        {
            var p = s + (float)i / stepCount * d;
            if (math.distancesq(obstaclePosition, p) < obstacleRadiusSq)
            {
                return true;
            }
        }
        return false;
    }
}


[BurstCompile]
partial struct ObstacleGenerationSystem : ISystem
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
                    var instance = cmd.Instantiate(config.ObstaclePrefab);
                    var obstacle = new Obstacle { radius = config.obstacleRadius };
                    cmd.AddComponent(instance, obstacle);

                    var position = new polar2
                    {
                        Theta = (j + offset) / (float)maxCount * (2f * Mathf.PI),
                        R = ringRadius
                    }.Cartesian2 + config.mapSize * .5f;
                    cmd.SetComponent(instance, new LocalToWorldTransform
                    {
                        Value = UniformScaleTransform.FromPosition(
                            math.float3(position, 0f)
                        ).ApplyScale(obstacle.radius)
                    });
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
        foreach (var c in SystemAPI.Query<ConfigurationComponent>())
        {
            GenerateObstacles(ref ecb, c);
        }
        state.Enabled = false;
    }
}



