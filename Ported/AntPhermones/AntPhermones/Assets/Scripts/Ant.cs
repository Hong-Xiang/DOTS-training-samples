using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;
using Unity.Collections;
using Unity.VisualScripting;

struct Ant : IComponentData
{
    public float facingAngle;
    public float speed;
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
                var position = new float2(UnityEngine.Random.Range(-5f, 5f), UnityEngine.Random.Range(-5f, 5f)) + c.mapSize * .5f;
                float facingAngle = UnityEngine.Random.Range(0.0f, math.PI * 2f);
                ecb.AddComponent(instance, new Ant { facingAngle = facingAngle, speed = 0.5f });
                ecb.SetComponent(instance, new LocalToWorldTransform
                {
                    Value = UniformScaleTransform.FromPosition(new float3(position.x, position.y, 0f))
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

        foreach (var (ant, transform) in SystemAPI.Query<RefRO<Ant>, TransformAspect>())
        {
            var speedv = new float2 { x = math.cos(ant.ValueRO.facingAngle), y = math.sin(ant.ValueRO.facingAngle) } * ant.ValueRO.speed;
            transform.Position += new float3 { x = speedv.x, y = speedv.y, z = 0f };
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


            foreach (var (ant, antTransform) in SystemAPI.Query<RefRW<Ant>, TransformAspect>())
            {
                var targetSpeed = config.antSpeed;

                ant.ValueRW.facingAngle += UnityEngine.Random.Range(-config.randomSteering, config.randomSteering);
                ant.ValueRW.speed += (targetSpeed - ant.ValueRW.speed) * config.antAccel;



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
