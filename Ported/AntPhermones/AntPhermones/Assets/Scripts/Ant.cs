using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;

struct AntVelocity : IComponentData
{
    public polar2 Velocity;
}

struct AntTarget : IComponentData
{
    public float2 Position;
}


struct AntSteer : IComponentData
{
    public float Value;
    public float Weight;
}

struct HoldingResource : IComponentData
{
}

struct SeeingTarget : IComponentData
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
                    },
                });
                ecb.AddComponent(instance, new AntSteer
                {
                    Value = 0f,
                    Weight = 0f
                });
            }
        }
        state.Enabled = false;
    }
}

// 仅有AntMoveSystem改变ant的Position
// 处理边界和障碍物的碰撞，因此也会改变ant的速度的方向
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntMoveSystem : ISystem
{
    MapBoundary mapBoundary;

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
            mapBoundary = new MapBoundary
            {
                X = math.float2(0f, c.mapSize),
                Y = math.float2(0f, c.mapSize),
            };
        }

        foreach (var (ant, transform) in SystemAPI.Query<RefRW<AntVelocity>, TransformAspect>())
        {
            var p = math.float2(transform.Position.x, transform.Position.y);
            var v = ant.ValueRO.Velocity.Cartesian2;

            (p, v) = mapBoundary.BoundaryCollision(v, p, p + v);

            // TODO: 利用临近关系加速obstacle查询
            foreach (var (o, t) in SystemAPI.Query<Obstacle, TransformAspect>())
            {
                (p, v) = o.BoundaryCollision(v, math.float2(t.Position.x, t.Position.y), p);
            }

            transform.Position = math.float3(p, transform.Position.z);

            var velo = ant.ValueRO.Velocity;
            velo.Theta = math.atan2(v.y, v.x);
            ant.ValueRW.Velocity = velo;
        }
    }
}

[BurstCompile]
partial struct AntTargetingSystem : ISystem
{
    float2 resourcePosition;
    float2 colonyPosition;
    float strength;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        strength = 1f;
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

        foreach (var resource in SystemAPI.Query<TransformAspect>().WithAll<Resource>())
        {
            resourcePosition = math.float2(resource.Position.xy);
        }
        foreach (var colony in SystemAPI.Query<TransformAspect>().WithAll<Colony>())
        {
            colonyPosition = math.float2(colonyPosition.xy);
        }

        foreach (var (t, s, e) in SystemAPI.Query<TransformAspect, RefRW<AntSteer>>().WithAll<HoldingResource>().WithEntityAccess())
        {
            if (math.distancesq(math.float2(t.Position.xy), colonyPosition) < 4f * 4f)
            {
                ecb.RemoveComponent<HoldingResource>(e);
                s.ValueRW.Value += math.PI * strength;
                s.ValueRW.Weight += strength;
            }
        }

        foreach (var (t, s, e) in SystemAPI.Query<TransformAspect, RefRW<AntSteer>>()
                                        .WithAll<AntVelocity>()
                                        .WithNone<HoldingResource>()
                                        .WithEntityAccess())
        {
            if (math.distancesq(math.float2(t.Position.x, t.Position.y), resourcePosition) < 4f * 4f)
            {
                ecb.AddComponent<HoldingResource>(e);
                s.ValueRW.Value += math.PI * strength;
                s.ValueRW.Weight += strength;
            }
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntRandomSteeringSystem : ISystem
{
    float strength;
    float maxAngle;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // maxAngle = math.PI * .125f;
        maxAngle = 1.0f;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            strength = config.randomSteering;
        }

        foreach (var s in SystemAPI.Query<RefRW<AntSteer>>())
        {
            var v = s.ValueRO;
            v.Value += UnityEngine.Random.Range(-maxAngle, maxAngle) * strength;
            v.Weight += strength;
            s.ValueRW = v;
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntGoalSteeringSystem : ISystem
{
    float strength;
    float2 resourcePosition;
    float2 colonyPosition;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    // TODO: 使用加速结构加速对Obstacle的求交
    [BurstCompile]
    bool Linecast(ref SystemState state, float2 point1, float2 point2)
    {
        var d = point2 - point1;
        float dist = math.length(d);

        int stepCount = (int)math.ceil(dist * .5f);
        for (int i = 0; i < stepCount; i++)
        {
            float t = (float)i / stepCount;
            var p = point1 + d * t;
            foreach (var (ot, o) in SystemAPI.Query<TransformAspect, Obstacle>())
            {
                if (math.distance(ot.Position, math.float3(p, 0f)) < o.radius)
                {
                    return true;
                }

            }
        }

        return false;
    }
    [BurstCompile]
    (float, float) SteerTowardsGoal(in float2 source, in float2 target, float facingAngle)
    {
        var targetAngle = math.atan2(target.y - source.y, target.x - source.x);
        var delta = (targetAngle - facingAngle);

        if (delta > math.PI)
        {
            return (math.PI * 2f, 1.0f);
        }
        else if (delta < -math.PI)
        {
            return (math.PI * 2f, 1.0f);
        }
        else
        {
            if (math.abs(delta) < math.PI * .5f)
                return (delta, strength);
        }

        // if (targetAngle - facingAngle > math.PI)
        // {
        //     return math.PI;
        // }
        // else if (targetAngle - facingAngle < -math.PI)
        // {
        //     return -math.PI;
        // }
        // else
        // {
        //     if (math.abs(targetAngle - facingAngle) < math.PI * .5f)
        //         return (targetAngle - facingAngle);
        // }

        return (0f, 0f);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            strength = config.goalSteerStrength;
        }
        foreach (var resource in SystemAPI.Query<TransformAspect>().WithAll<Resource>())
        {
            resourcePosition = math.float2(resource.Position.x, resource.Position.y);
        }
        foreach (var colony in SystemAPI.Query<TransformAspect>().WithAll<Colony>())
        {
            colonyPosition = math.float2(colony.Position.x, colony.Position.y);
        }

        foreach (var (steer, transform, velocity) in SystemAPI.Query<RefRW<AntSteer>, TransformAspect, AntVelocity>()
                                                              .WithAll<HoldingResource>())
        {
            var position = math.float2(transform.Position.x, transform.Position.y);
            if (!Linecast(ref state, position, colonyPosition))
            {
                var (d, w) = SteerTowardsGoal(position, colonyPosition, velocity.Velocity.Theta);
                var v = steer.ValueRO;
                v.Value += d * w;
                v.Weight += w;
                steer.ValueRW = v;
            }

        }

        foreach (var (steer, transform, velocity) in SystemAPI.Query<RefRW<AntSteer>, TransformAspect, AntVelocity>()
                                                              .WithNone<HoldingResource>())
        {
            var position = math.float2(transform.Position.x, transform.Position.y);
            if (!Linecast(ref state, position, colonyPosition))
            {
                var (d, w) = SteerTowardsGoal(position, resourcePosition, velocity.Velocity.Theta);
                var v = steer.ValueRO;
                v.Value += d * w;
                v.Weight += w;
                steer.ValueRW = v;
            }
        }
    }
}


partial class PheromoneSystem : SystemBase
{
    protected override void OnUpdate()
    {
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntManagerSystem : ISystem
{
    float speed;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    float PheromoneSteering(in polar2 velocity, in float2 position, float distance, int mapSize)
    {
        float output = 0;
        var facingAngle = velocity.Theta;

        for (int i = -1; i <= 1; i += 2)
        {
            float angle = facingAngle + i * math.PI * .25f;
            float testX = position.x + math.cos(angle) * distance;
            float testY = position.y + math.sin(angle) * distance;

            if (testX < 0 || testY < 0 || testX >= mapSize || testY >= mapSize)
            {

            }
            else
            {
                // int index = PheromoneIndex((int)testX, (int)testY);
                // float value = pheromones[index].r;
                var value = 0f;
                output += value * i;
            }
        }
        return math.sign(output);
    }

    int WallSteering(in polar2 velocity, in float2 position, float distance, int mapSize)
    {
        int output = 0;

        for (int i = -1; i <= 1; i += 2)
        {
            float angle = velocity.Theta + i * math.PI * .25f;
            float testX = position.x + math.cos(angle) * distance;
            float testY = position.y + math.sin(angle) * distance;

            if (testX < 0 || testY < 0 || testX >= mapSize || testY >= mapSize)
            {

            }
            else
            {
                // int value = GetObstacleBucket(testX, testY).Length;
                var value = 0;
                if (value > 0)
                {
                    output -= i;
                }
            }
        }
        return output;
    }



    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            foreach (var resourceTransform in SystemAPI.Query<TransformAspect>().WithAll<Resource>())
            {
                var resourcePositoin = math.float2(resourceTransform.Position.x, resourceTransform.Position.y);

                foreach (var colonyTransform in SystemAPI.Query<TransformAspect>().WithAll<Colony>())
                {
                    var colonyPosition = math.float2(colonyTransform.Position.x, colonyTransform.Position.y);
                    foreach (var (ant, transform) in SystemAPI.Query<RefRW<AntVelocity>, TransformAspect>())
                    {
                        var targetSpeed = config.antSpeed;
                        var velocity = ant.ValueRO.Velocity;

                        // velocity.Theta += UnityEngine.Random.Range(-config.randomSteering, config.randomSteering);
                        var v = velocity.Cartesian2;


                        var position = math.float2(transform.Position.x, transform.Position.y);
                        // TODO: implement steering
                        // float pheroSteering = PheromoneSteering(ant, 3f);
                        // int wallSteering = WallSteering(ant, 1.5f);
                        // ant.facingAngle += pheroSteering * pheromoneSteerStrength;
                        // ant.facingAngle += wallSteering * wallSteerStrength;

                        // targetSpeed *= 1f - (Mathf.Abs(pheroSteering) + Mathf.Abs(wallSteering)) / 3f;


                        // float2 targetPos;
                        // // int index1 = i / instancesPerBatch;
                        // // int index2 = i % instancesPerBatch;
                        // if (holdingResource == false)
                        // {
                        //     targetPos = math.float2(resourceTransform.Position.x, resourceTransform.Position.y);
                        //     // antColors[index1][index2] += ((Vector4)searchColor * ant.brightness - antColors[index1][index2]) * .05f;
                        // }
                        // else
                        // {
                        //     targetPos = math.float2(colonyTransform.Position.x, colonyTransform.Position.y);
                        //     // antColors[index1][index2] += ((Vector4)carryColor * ant.brightness - antColors[index1][index2]) * .05f;
                        // }

                        // if (math.lengthsq(position - targetPos) < 4f * 4f)
                        // {
                        //     holdingResource = !holdingResource;
                        //     velocity.Theta += math.PI;
                        // }

                        v = velocity.Cartesian2;

                        // var updatedPosition = position + v;
                        // (position, v) = mapBoundary.BoundaryCollision(v, position, position + v);




                        // foreach (var (o, t) in SystemAPI.Query<Obstacle, TransformAspect>())
                        // {
                        //     (position, v) = o.BoundaryCollision(v, math.float2(t.Position.x, t.Position.y), position);
                        // }









                        // if (math.any(velocity.Cartesian2 != v))
                        // {
                        //     velocity.Theta = math.atan2(v.y, v.x);
                        // }

                        // if (!holdingResource)
                        // {
                        //     float excitement = 1f - math.clamp(math.distance(targetPos, position) / (config.mapSize * 1.2f), 0f, 1f);
                        //     if (holdingResource)
                        //     {
                        //         excitement = 1f;
                        //     }
                        //     // excitement *= ant.speed / antSpeed;
                        //     // DropPheromones(ant.position, excitement);
                        // }

                        // 渲染用
                        // Matrix4x4 matrix = GetRotationMatrix(ant.facingAngle);
                        // matrix.m03 = ant.position.x / mapSize;
                        // matrix.m13 = ant.position.y / mapSize;
                        // matrices[i / instancesPerBatch][i % instancesPerBatch] = matrix;

                    }


                    // for (int x = 0; x < mapSize; x++)
                    // {
                    //     for (int y = 0; y < mapSize; y++)
                    //     {
                    //         int index = PheromoneIndex(x, y);
                    //         pheromones[index].r *= trailDecay;
                    //     }
                    // }

                    // pheromoneTexture.SetPixels(pheromones);
                    // pheromoneTexture.Apply();

                    // for (int i = 0; i < matProps.Length; i++)
                    // {
                    //     matProps[i].SetVectorArray("_Color", antColors[i]);
                    // }




                }
            }
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct AntAccelerationSystem : ISystem
{
    AntSteer zeroSteering;
    float maxSpeed;
    float acceleration;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        zeroSteering = new AntSteer
        {
            Value = 0f,
            Weight = 0f
        };
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            maxSpeed = config.antSpeed;
            acceleration = config.antAccel;
        }

        foreach (var (antV, antS) in SystemAPI.Query<RefRW<AntVelocity>, RefRW<AntSteer>>())
        {
            var targetSpeed = maxSpeed;
            var velocity = antV.ValueRO.Velocity;

            // targetSpeed *= 1f - (Mathf.Abs(pheroSteering) + Mathf.Abs(wallSteering)) / 3f;
            targetSpeed *= 1f;

            velocity.R += (targetSpeed - velocity.R) * acceleration;

            var s = antS.ValueRO;
            if (s.Weight > 0f)
            {
                // velocity.Theta += s.Value / s.Weight;
                velocity.Theta += s.Value;
            }


            antS.ValueRW = zeroSteering;
            antV.ValueRW.Velocity = velocity;
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
partial struct PushInwardOutwardSystem : ISystem
{
    float inwardStrength;
    float outwardStrength;
    int mapSize;
    float2 colonyPosition;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    float2 InwardOutwardVelocity(
         in float2 velocity,
         float inwardOrOutward,
         float pushRadius,
         in float2 position)
    {
        var d = colonyPosition - position;
        var dist = math.length(d);
        inwardOrOutward *= 1f - math.clamp(dist / pushRadius, 0f, 1f);
        return velocity + math.normalize(d) * inwardOrOutward;
    }



    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {

        foreach (var config in SystemAPI.Query<ConfigurationComponent>())
        {
            inwardStrength = config.inwardStrength;
            outwardStrength = config.outwardStrength;
            mapSize = config.mapSize;
        }
        foreach (var colony in SystemAPI.Query<TransformAspect>().WithAll<Colony>())
        {
            colonyPosition = math.float2(colony.Position.x, colony.Position.y);
        }

        foreach (var (ant, trans) in SystemAPI.Query<RefRW<AntVelocity>, TransformAspect>().WithAll<HoldingResource>())
        {
            var position = math.float2(trans.Position.x, trans.Position.y);
            var velocity = ant.ValueRO.Velocity;
            var v = velocity.Cartesian2;
            v = InwardOutwardVelocity(v, inwardStrength, (float)mapSize, position);
            velocity.Theta = math.atan2(v.y, v.x);
            ant.ValueRW.Velocity = velocity;
        }

        foreach (var (ant, trans) in SystemAPI.Query<RefRW<AntVelocity>, TransformAspect>().WithNone<HoldingResource>())
        {
            var position = math.float2(trans.Position.x, trans.Position.y);
            var velocity = ant.ValueRO.Velocity;
            var v = velocity.Cartesian2;
            v = InwardOutwardVelocity(v, -outwardStrength, mapSize * .4f, position);
            velocity.Theta = math.atan2(v.y, v.x);
            ant.ValueRW.Velocity = velocity;
        }
    }
}
