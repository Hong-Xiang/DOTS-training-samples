using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;
using Unity.Collections;
using Unity.VisualScripting;

struct AntVelocity : IComponentData
{
    public polar2 Velocity; // (r, theta) in polar coordinate
}

[BurstCompile]
struct polar2
{
    public float2 Value;

    public float R
    {
        get => Value[0];
        set => Value[0] = value;
    }

    public float Theta
    {
        get => Value[1];
        set => Value[1] = value;
    }

    public float X
    {
        get => R * math.cos(Theta);
    }

    public float Y
    {
        get => R * math.sin(Theta);
    }

    public float2 Cartesian2
    {
        get => math.float2(X, Y);
        set
        {
            R = math.length(value);
            Theta = math.atan2(value.y, value.x);
        }
    }
}

[BurstCompile]
partial struct MapBoundary
{
    public float2 X;
    public float2 Y;



    public float2 BoundaryCollision(ref float2 velocity, in float2 previousPosition)
    {
        var updatedPosition = previousPosition + velocity;
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
        return updatedPosition;
    }
}


struct HoldingResource : IComponentData
{
}

struct Brightness : IComponentData
{
    public float intensity;
}

[BurstCompile]
partial struct AntSpwanSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var c in SystemAPI.Query<ConfigurationComponent>())
        {
            for (var i = 0; i < c.antCount; i++)
            {
                var instance = ecb.Instantiate(c.AntPrefab);
                var position = math.float2(UnityEngine.Random.Range(-5f, 5f), UnityEngine.Random.Range(-5f, 5f)) + c.mapSize * .5f;
                ecb.SetComponent(instance, new LocalToWorldTransform
                {
                    Value = UniformScaleTransform.FromPosition(math.float3(position, 0f))
                });
                ecb.AddComponent(instance, new AntVelocity
                {
                    Velocity = new polar2
                    {
                        Theta = UnityEngine.Random.Range(0.0f, math.PI * 2f),
                        R = 0.5f
                    }
                });
            }
        }
        state.Enabled = false;
    }
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntMoveSystem : ISystem
{
    float speed;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {

        foreach (var c in SystemAPI.Query<ConfigurationComponent>())
        {
            var mapBoundary = new MapBoundary
            {
                X = math.float2(0f, c.mapSize),
                Y = math.float2(0f, c.mapSize),
            };
            foreach (var (ant, transform) in SystemAPI.Query<RefRW<AntVelocity>, TransformAspect>())
            {
                var p = math.float2(transform.Position.x, transform.Position.y);
                var v = ant.ValueRO.Velocity.Cartesian2;

                p = mapBoundary.BoundaryCollision(ref v, p);


                // TODO: check obstacle collision
                foreach (var (o, t) in SystemAPI.Query<Obstacle, TransformAspect>())
                {
                    p = o.BoundaryCollision(ref v, math.float2(t.Position.x, t.Position.y), p);
                }

                ant.ValueRW.Velocity.Cartesian2 = v;
                transform.Position = math.float3(p, transform.Position.z);
            }
        }

    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntSteeringSystem : ISystem
{
    [BurstCompile]
    int WallSteering(float2 position, float facingAngle, float3 obstaclePosition, float distance)
    {
        int output = 0;
        for (int i = -1; i <= 1; i += 2)
        {
            //var rotatedForward = math.rotate(quaternion.RotateY(i * Mathf.PI * .25f), antForwad);
            var angle = facingAngle + i * math.PI * .25f;
            var testDirection = new float3(math.cos(angle), math.sin(angle), 0.0f);
            var testPosition = new float3(position.x, position.y, 0.0f) + testDirection * distance;
            var obstacleDistance = math.distance(testPosition, obstaclePosition);

            if (obstacleDistance <= distance)
            {
                output -= i;
            }
        }
        return output;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float wallSteeringStrenth = 0.1f;
        float obstacleRadius = 3.0f;
        // desired API
        // from (ant, antTransform) in SystemAPI.Query<Ant, TransformAspect>()
        // from obstacleTransform in SystemAPI.Query<TransformAspect>()
        //  .WithAll<Obstacle>().Where(o => math.lengthsq(antTransform.Position - obstacleTransform.Position) < obstacleRadius)
        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            foreach (var resource in SystemAPI.Query<TransformAspect>().WithAll<ResourceComponent>())
            {
                foreach (var ant in SystemAPI.Query<RefRW<AntVelocity>>())
                {
                    var targetSpeed = config.antSpeed;

                    ant.ValueRW.Velocity.Theta += UnityEngine.Random.Range(-config.randomSteering, config.randomSteering);
                    ant.ValueRW.Velocity.R += (targetSpeed - ant.ValueRO.Velocity.R) * config.antAccel;


                    //foreach (var obstacleTransform in SystemAPI.Query<TransformAspect>().WithAll<Obstacle>())
                    //{
                    //    WallSteering(
                    //        ant.position,
                    //        ant.facingAngle,
                    //        obstacleTransform.Position,
                    //        obstacleRadius);
                    //}
                    //var rotateSpeed = 0.05f;
                    //transform.RotateWorld(quaternion.RotateY(UnityEngine.Random.Range(-math.PI * rotateSpeed, math.PI * rotateSpeed)));
                }
            }
        }
    }
}
