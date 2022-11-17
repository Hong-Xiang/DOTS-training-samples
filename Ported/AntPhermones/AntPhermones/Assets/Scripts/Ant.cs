using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;

struct AntVelocity : IComponentData
{
    public polar2 Velocity; // (r, theta) in polar coordinate
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
            foreach (var resource in SystemAPI.Query<TransformAspect>().WithAll<Resource>())
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
