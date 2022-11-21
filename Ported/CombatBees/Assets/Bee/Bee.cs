using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

public struct BeeComponent : IComponentData
{
    public float3 smoothPosition;
    public float3 smoothDirection;
    public int team;
    public float size;
    public float deathTimer;
    public bool isAttacking;
    public bool isHoldingResource;
    public int index;
}



public struct SmoothPositionVelociy : IComponentData
{
    public float3 Position;
    public float3 Velocity;
}

public partial struct Team : ISharedComponentData
{
    public int Value;
}


public partial struct EnemyTarget : IComponentData
{
    public Entity Target;
}

public partial struct ResourceTarget : IComponentData
{
    public Entity Target;
}

public partial struct Velocity : IComponentData
{
    public float3 Value;
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeeDeathSystem))]
public partial struct BeeRandomWalkSystem : ISystem
{
    public Unity.Mathematics.Random random;

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (bee, transform, velocity) in SystemAPI.Query<
            RefRW<BeeComponent>,
            TransformAspect,
            RefRW<Velocity>>().WithNone<Death>())
        {
            bee.ValueRW.isAttacking = false;
            bee.ValueRW.isHoldingResource = false;
            var v = velocity.ValueRO.Value;
            v += (random.NextFloat3Direction()) * (config.flightJitter * deltaTime);
            v *= (1f - config.damping);

            velocity.ValueRW.Value = v;
        }
    }
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(42);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

}

public partial struct BeeAliasSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        // var allies =  teamsOfBees[bee.team];
        // var attractiveFriend = allies[random.NextInt(0, allies.Count)];
        // var delta = attractiveFriend.position - transform.Position;
        // var delta = float3.zero;
        // float dist = math.length(delta);
        // if (dist > 0f)
        // {
        //     v += delta * (config.teamAttraction * deltaTime / dist);
        // }

        // Bee repellentFriend = allies[random.NextInt(0, allies.Count)];
        // delta = attractiveFriend.position - bee.position;
        // dist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
        // if (dist > 0f)
        // {
        //     bee.velocity -= delta * (teamRepulsion * deltaTime / dist);
        // }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeEnemyTargetSystem))]
[UpdateAfter(typeof(BeeResourceTargetSystem))]
public partial struct BeeNewTargetSystem : ISystem
{
    Unity.Mathematics.Random random;


    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(233);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var q = SystemAPI.QueryBuilder().WithAll<BeeComponent, Team>().Build();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        var aliasTeam = 0;
        var enemyTeam = 1 - aliasTeam;

        q.SetSharedComponentFilter(new Team { Value = enemyTeam });
        var es = q.ToEntityArray(Allocator.Temp);
        foreach (var (b, e) in SystemAPI.Query<BeeComponent>()
                                                .WithNone<EnemyTarget, ResourceTarget>()
                                                .WithEntityAccess()
                                                .WithSharedComponentFilter(new Team { Value = aliasTeam }))
        {
            if (random.NextFloat() < config.aggression)
            {
                // 这里的es.Length > 0判定在现在的模式下可以提到循环外，此处为了和原始代码的对齐保持在这里
                if (es.Length > 0)
                {
                    ecb.AddComponent(e, new EnemyTarget { Target = es[random.NextInt(0, es.Length)] });
                }
            }
            else
            {
                //             bee.resourceTarget = ResourceSystem.TryGetRandomResource();
            }
        }
        es.Dispose();



        aliasTeam = 1;
        enemyTeam = 1 - aliasTeam;


        q.SetSharedComponentFilter(new Team { Value = enemyTeam });
        var es2 = q.ToEntityArray(Allocator.Temp);
        foreach (var (b, e) in SystemAPI.Query<BeeComponent>()
                                                .WithNone<EnemyTarget, ResourceTarget>()
                                                .WithEntityAccess()
                                                .WithSharedComponentFilter(new Team { Value = aliasTeam }))
        {
            if (random.NextFloat() < config.aggression)
            {
                if (es2.Length > 0)
                {
                    ecb.AddComponent(e, new EnemyTarget { Target = es2[random.NextInt(0, es2.Length)] });
                }
            }
            else
            {
                //             bee.resourceTarget = ResourceSystem.TryGetRandomResource();
            }
        }
        es2.Dispose();


    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct BeeEnemyTargetSystem : ISystem
{
    Unity.Mathematics.Random random;
    ComponentLookup<LocalToWorldTransform> LocalToWorldTransformFromEntity;
    ComponentLookup<Velocity> VelocityFromEntity;
    ComponentLookup<Death> DeathFromEntity;


    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
        LocalToWorldTransformFromEntity = state.GetComponentLookup<LocalToWorldTransform>(true);
        VelocityFromEntity = state.GetComponentLookup<Velocity>(true);
        DeathFromEntity = state.GetComponentLookup<Death>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {

        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        LocalToWorldTransformFromEntity.Update(ref state);
        VelocityFromEntity.Update(ref state);
        DeathFromEntity.Update(ref state);

        var deltaTime = SystemAPI.Time.DeltaTime;



        foreach (var (bee, target, transform, velocity, e) in SystemAPI.Query<RefRW<BeeComponent>, EnemyTarget, TransformAspect, RefRW<Velocity>>().WithNone<Death>().WithEntityAccess())
        {
            // 备注：大问题 - ECS没有引用完整性的保证，对于可能被Destory的Entity，引用的地方会有运行时异常
            // 比如如下的SystemAPI.Exists如果不检查，LocalToWorldTransformFromEntity就运行时异常了
            // 方案1：添加防御性检查，但是其实会让系统留下不正确的状态，即破坏了一致性，认为不是好方案
            // 方案2：在Destory Entity的时候删除所有引用，保证系统的一致性
            // 由于没有反向index，方案2需要完整遍历所有Entity，性能不合适，因此这里用了方案1
            if ((!SystemAPI.Exists(target.Target)) || DeathFromEntity.HasComponent(target.Target))
            {
                ecb.RemoveComponent<EnemyTarget>(e);
            }
            else
            {
                var enemyPosition = LocalToWorldTransformFromEntity[target.Target].Value.Position;
                var delta = enemyPosition - transform.Position;
                var distance = math.length(delta);
                var normalizedDelta = delta / distance;
                if (distance > config.attackDistance)
                {
                    velocity.ValueRW.Value += normalizedDelta * (config.chaseForce * deltaTime);
                }
                else
                {
                    bee.ValueRW.isAttacking = true;
                    velocity.ValueRW.Value += normalizedDelta * (config.attackForce * deltaTime);
                    if (distance < config.hitDistance)
                    {
                        // ParticleManager.SpawnParticle(bee.enemyTarget.position, ParticleType.Blood, bee.velocity * .35f, 2f, 6);
                        ecb.AddComponent(target.Target, new Death { });

                        ecb.SetComponent(target.Target, new Velocity
                        {
                            Value = VelocityFromEntity[target.Target].Value * .5f
                        });

                        ecb.RemoveComponent<EnemyTarget>(e);
                    }
                }
            }
        }
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct BeeResourceTargetSystem : ISystem
{
    Unity.Mathematics.Random random;


    public void OnCreate(ref SystemState state)
    {
        random = new Unity.Mathematics.Random(233);
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        foreach (var (b, e) in SystemAPI.Query<BeeComponent>().WithNone<EnemyTarget, ResourceTarget, Death>().WithEntityAccess())
        {
            //         Resource resource = bee.resourceTarget;
            //         if (resource.holder == null)
            //         {
            //             if (resource.dead)
            //             {
            //                 bee.resourceTarget = null;
            //             }
            //             else if (resource.stacked && ResourceSystem.IsTopOfStack(resource) == false)
            //             {
            //                 bee.resourceTarget = null;
            //             }
            //             else
            //             {
            //                 delta = resource.position - bee.position;
            //                 float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
            //                 if (sqrDist > grabDistance * grabDistance)
            //                 {
            //                     bee.velocity += delta * (chaseForce * deltaTime / Mathf.Sqrt(sqrDist));
            //                 }
            //                 else if (resource.stacked)
            //                 {
            //                     ResourceSystem.GrabResource(bee, resource);
            //                 }
            //             }
            //         }
            //         else if (resource.holder == bee)
            //         {
            //             Vector3 targetPos = new Vector3(-Field.size.x * .45f + Field.size.x * .9f * bee.team, 0f, bee.position.z);
            //             delta = targetPos - bee.position;
            //             dist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
            //             bee.velocity += (targetPos - bee.position) * (carryForce * deltaTime / dist);
            //             if (dist < 1f)
            //             {
            //                 resource.holder = null;
            //                 bee.resourceTarget = null;
            //             }
            //             else
            //             {
            //                 bee.isHoldingResource = true;
            //             }
            //         }
            //         else if (resource.holder.team != bee.team)
            //         {
            //             bee.enemyTarget = resource.holder;
            //         }
            //         else if (resource.holder.team == bee.team)
            //         {
            //             bee.resourceTarget = null;
            //         }

        }
    }
}



[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeRandomWalkSystem))]
[UpdateBefore(typeof(BeeDeathSystem))]
public partial struct BeeMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var resourceConfig = SystemAPI.GetSingleton<ResourceConfiguration>();
        foreach (var (bee, velocity, transform, smoothPosition) in SystemAPI.Query<BeeComponent, RefRW<Velocity>, TransformAspect, RefRW<SmoothPositionVelociy>>())
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var v = velocity.ValueRO.Value;
            transform.Position += deltaTime * v;
            var position = transform.Position;

            if (math.abs(position.x) > Field.size.x * .5f)
            {
                position.x = (Field.size.x * .5f) * math.sign(position.x);
                v.x *= -.5f;
                v.y *= .8f;
                v.z *= .8f;
            }
            if (math.abs(position.z) > Field.size.z * .5f)
            {
                position.z = (Field.size.z * .5f) * math.sign(position.z);
                v.z *= -.5f;
                v.x *= .8f;
                v.y *= .8f;
            }

            var resourceModifier = bee.isHoldingResource ? resourceConfig.resourceSize : 0f;
            if (math.abs(position.y) > Field.size.y * .5f - resourceModifier)
            {
                position.y = (Field.size.y * .5f - resourceModifier) * math.sign(position.y);
                v.y *= -.5f;
                v.z *= .8f;
                v.x *= .8f;
            }
            transform.Position = position;
            velocity.ValueRW.Value = v;


            // // only used for smooth rotation:
            var oldPosition = smoothPosition.ValueRO.Position;
            var updatedSmoothPosition = bee.isAttacking ? transform.Position :
                math.lerp(oldPosition, transform.Position, deltaTime * config.rotationStiffness);
            smoothPosition.ValueRW.Position = updatedSmoothPosition;
            smoothPosition.ValueRW.Velocity = updatedSmoothPosition - oldPosition;
        }

        // for (int i = 0; i < bees.Count; i++)
        // {
        //     float size = bees[i].size;
        //     Vector3 scale = new Vector3(size, size, size);
        //     if (bees[i].dead == false)
        //     {
        //         float stretch = Mathf.Max(1f, bees[i].velocity.magnitude * speedStretch);
        //         scale.z *= stretch;
        //         scale.x /= (stretch - 1f) / 5f + 1f;
        //         scale.y /= (stretch - 1f) / 5f + 1f;
        //     }
        //     Quaternion rotation = Quaternion.identity;
        //     if (bees[i].smoothDirection != Vector3.zero)
        //     {
        //         rotation = Quaternion.LookRotation(bees[i].smoothDirection);
        //     }
        //     // Color color = teamColors[bees[i].team];
        //     var color = Color.red;
        //     if (bees[i].dead)
        //     {
        //         color *= .75f;
        //         scale *= Mathf.Sqrt(bees[i].deathTimer);
        //     }
        // beeMatrices[i / beesPerBatch][i % beesPerBatch] = Matrix4x4.TRS(bees[i].position, rotation, scale);
        // beeColors[i / beesPerBatch][i % beesPerBatch] = color;
        // }
        // for (int i = 0; i <= activeBatch; i++)
        // {
        //     if (beeMatrices[i].Count > 0)
        //     {
        //         matProps.SetVectorArray("_Color", beeColors[i]);
        //         Graphics.DrawMeshInstanced(beeMesh, 0, beeMaterial, beeMatrices[i], matProps);
        //     }
        // }
    }
}

public partial struct BeeSpawnSystem : ISystem
{
    Unity.Mathematics.Random random;

    void SpawnBee(ref EntityCommandBuffer ECB, float3 pos, int team, in Entity prefab, float2 sizeRange, Color team0Color, Color team1Color, float3 maxSpawnSpeed)
    {
        var instance = ECB.Instantiate(prefab);
        var size = random.NextFloat(sizeRange[0], sizeRange[1]);
        ECB.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(pos).ApplyScale(size)
        });

        var velocity = random.NextFloat3Direction() * maxSpawnSpeed;
        ECB.AddComponent(instance, new Velocity { Value = velocity });

        ECB.AddSharedComponent(instance, new Team { Value = team });
        var color = team == 0 ? team0Color : team1Color;
        ECB.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = math.float4(color.r, color.g, color.b, color.a) });
        ECB.AddComponent(instance, new BeeComponent
        {
            deathTimer = 1f,
        });
        ECB.AddComponent(instance, new SmoothPositionVelociy { });
    }
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();

        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        for (int i = 0; i < config.startBeeCount; i++)
        {
            int team = i % 2;

            Vector3 pos = Vector3.right * (-Field.size.x * .4f + Field.size.x * .8f * team);
            SpawnBee(ref ecb, pos, team, config.BeePrefab, math.float2(config.minBeeSize, config.maxBeeSize), config.teamAColor, config.teamBColor, config.maxSpawnSpeed);
        }

        state.Enabled = false;

    }
}


public partial struct Death : IComponentData { }

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeNewTargetSystem))]
public partial struct BeeDeathSystem : ISystem
{
    Unity.Mathematics.Random random;
    public void OnCreate(ref SystemState state)
    {
        random = Unity.Mathematics.Random.CreateFromIndex(233);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        foreach (var (bee, velocity, e) in SystemAPI.Query<RefRW<BeeComponent>, RefRW<Velocity>>().WithAll<Death>().WithEntityAccess())
        {
            if (random.NextFloat() < (bee.ValueRO.deathTimer - .5f) * .5f)
            {
                // ParticleManager.SpawnParticle(bee.position, ParticleType.Blood, Vector3.zero);
            }

            var v = velocity.ValueRO.Value;
            v.y += Field.gravity * deltaTime;
            velocity.ValueRW.Value = v;

            bee.ValueRW.deathTimer -= deltaTime / 10f;
            if (bee.ValueRW.deathTimer < 0f)
            {
                ecb.DestroyEntity(e);
            }
        }
    }
}