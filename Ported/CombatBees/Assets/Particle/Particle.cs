using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;
using Unity.Collections;
using Unity.Rendering;

partial struct Particle : IComponentData
{
    public float3 size;
}

partial struct ParticleLife : IComponentData
{
    public float normalizedLife; // always within [0, 1]
    public float Duration;
}

partial struct StuckedParticle : IComponentData
{
}

partial struct BloodParticle : IComponentData { }
partial struct SpawnParticle : IComponentData { }


// [BurstCompile]
partial struct ParticleSpawner
{
    [ReadOnly] public ParticleConfiguration config;


    // [BurstCompile]
    public void SpawnParticleSpawnFlash(ref Random random,
                                        EntityCommandBuffer ecb,
                                        float3 position,
                                        float3 velocity)
    {
        var instance = ecb.Instantiate(config.particlePrefab);

        ecb.SetComponent(instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position)
        });
        ecb.AddComponent(instance, new PostTransformMatrix { Value = float4x4.identity });
        ecb.AddComponent(instance, new Velocity { Value = velocity });

        ecb.AddComponent(instance, new Particle
        {
            size = math.float3(random.NextFloat(1f, 2f)),
        });
        ecb.AddComponent(instance, new ParticleLife
        {
            normalizedLife = 1f,
            Duration = random.NextFloat(.25f, .5f)
        });
        ecb.AddComponent(instance, new SpawnParticle { });

        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = math.float4(1f) });
    }

    // [BurstCompile]
    public void SpawnParticleBlood(ref Random random,
                                   in int sortKey,
                                   EntityCommandBuffer.ParallelWriter ecb,
                                   float3 position,
                                   float3 velocity)
    {
        var instance = ecb.Instantiate(sortKey, config.particlePrefab);

        ecb.SetComponent(sortKey, instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position)
        });
        ecb.AddComponent(sortKey, instance, new PostTransformMatrix { Value = float4x4.identity });
        ecb.AddComponent(sortKey, instance, new Velocity
        {
            Value = velocity
        });

        ecb.AddComponent(sortKey, instance, new Particle
        {
            size = math.float3(1f) * random.NextFloat(.1f, .2f),
        });
        ecb.AddComponent(sortKey, instance, new ParticleLife
        {
            normalizedLife = 1f,
            Duration = random.NextFloat(3f, 5f)
        });
        ecb.AddComponent(sortKey, instance, new BloodParticle { });

        var hsv = random.NextFloat3(
                    math.float3(-.05f, .75f, .3f),
                    math.float3(.05f, 1f, .8f));
        var rgb = UnityEngine.Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
        ecb.AddComponent(sortKey, instance, new URPMaterialPropertyBaseColor { Value = math.float4(rgb.r, rgb.g, rgb.b, 1f) });
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
partial struct ParticleSpawnSystem : ISystem
{
    EntityQuery ParticleQuery;
    Unity.Mathematics.Random random;

    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
        ParticleQuery = state.GetEntityQuery(typeof(Particle));
        random = Unity.Mathematics.Random.CreateFromIndex(42);

        state.RequireForUpdate<ParticleConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);
        var config = SystemAPI.GetSingleton<ParticleConfiguration>();
        var ps = new ParticleSpawner
        {
            config = config
        };
        if (ParticleQuery.CalculateEntityCount() > config.maxParticleCount)
        {
            return;
        }
        for (int i = 0; i < 100; i++)
        {
            var v = random.NextFloat3Direction() * 6f;
            ps.SpawnParticleBlood(ref random, 0, ecb.AsParallelWriter(), float3.zero, v);

        }
        state.Enabled = false;
    }
}

[WithNone(typeof(StuckedParticle))]
partial struct ParticleSimulationJob : IJobEntity
{
    [ReadOnly] public FieldComponent field;
    public EntityCommandBuffer.ParallelWriter ECB;
    public float deltaTime;
    [BurstCompile]
    void Execute(ref TransformAspect transform, ref Velocity velocity, ref Particle particle, in Entity entity, [EntityInQueryIndex] int inQueryIndex)
    {
        velocity.Value += math.float3(0f, 1f, 0f) * (field.Gravity * deltaTime);
        var position = transform.Position;
        position += velocity.Value * deltaTime;

        var stucked = false;

        if (math.abs(position.x) > field.Size.x * .5f)
        {
            position.x = field.Size.x * .5f * math.sign(position.x);
            float splat = math.abs(velocity.Value.x * .3f) + 1f;
            particle.size.y *= splat;
            particle.size.z *= splat;
            stucked = true;
        }
        if (math.abs(position.y) > field.Size.y * .5f)
        {
            position.y = field.Size.y * .5f * math.sign(position.y);
            float splat = math.abs(velocity.Value.y * .3f) + 1f;
            particle.size.z *= splat;
            particle.size.x *= splat;
            stucked = true;
        }
        if (math.abs(position.z) > field.Size.z * .5f)
        {
            position.z = field.Size.z * .5f * math.sign(position.z);
            float splat = math.abs(velocity.Value.z * .3f) + 1f;
            particle.size.x *= splat;
            particle.size.y *= splat;
            stucked = true;
        }

        if (stucked)
        {
            ECB.AddComponent(inQueryIndex, entity, new StuckedParticle { });
            velocity.Value = float3.zero;
            // particle.cachedMatrix = Matrix4x4.TRS(particle.position, Quaternion.identity, particle.size);

        };
        transform.Position = position;
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct ParticleSimulationSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FieldComponent>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var field = SystemAPI.GetSingleton<FieldComponent>();
        var ecb = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        state.Dependency = new ParticleSimulationJob
        {
            ECB = ecb.AsParallelWriter(),
            deltaTime = deltaTime,
            field = field
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
partial struct ParticleRemoveJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ECB;
    public float DeltaTime;

    [BurstCompile]
    void Execute(ref ParticleLife life, in Entity entity, [EntityInQueryIndex] int inQueryIndex)
    {
        life.normalizedLife -= DeltaTime / life.Duration;
        if (life.normalizedLife < 0f)
        {
            ECB.DestroyEntity(inQueryIndex, entity);
        }
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct ParticleRemoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ECB = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);
        var deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new ParticleRemoveJob
        {
            ECB = ECB.AsParallelWriter(),
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(BloodParticle))]
[WithNone(typeof(StuckedParticle))]
partial struct BloodParticlePresentJob : IJobEntity
{
    [ReadOnly] public ParticleConfiguration config;
    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
                     ref URPMaterialPropertyBaseColor color,
                     in Particle particle,
                     in Velocity velocity,
                     in ParticleLife life)
    {
        float3 scale = math.float3(particle.size * life.normalizedLife);
        var rotation = quaternion.LookRotation(velocity.Value, math.float3(0f, 1f, 0f));
        scale.z *= 1f + math.length(velocity.Value) * config.speedStretch;
        matrix.Value = float4x4.TRS(
            float3.zero,
            rotation,
            scale
        );
        var c = color.Value;
        color.Value.w = life.normalizedLife;
    }
}

[BurstCompile]
[WithAll(typeof(SpawnParticle))]
[WithNone(typeof(StuckedParticle))]
partial struct SpawnParticlePresentJob : IJobEntity
{
    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
                     ref URPMaterialPropertyBaseColor color,
                     in Particle particle,
                     in Velocity velocity,
                     in ParticleLife life)
    {
        matrix.Value = float4x4.Scale(particle.size * life.normalizedLife);
        color.Value.w = life.normalizedLife;
    }
}

[WithAll(typeof(StuckedParticle))]
[BurstCompile]
partial struct StuckedParticlePresentJob : IJobEntity
{
    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
                     ref URPMaterialPropertyBaseColor color,
                     in Particle particle,
                     in ParticleLife life)
    {
        matrix.Value = float4x4.Scale(particle.size * life.normalizedLife);
        color.Value.w = life.normalizedLife;
    }
}

// TODO: 处理Z方向scale不稳定的问题
[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct ParticlePresentSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ParticleConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ParticleConfiguration>();
        state.Dependency = new BloodParticlePresentJob
        {
            config = config
        }.Schedule(state.Dependency);
        state.Dependency = new SpawnParticlePresentJob { }.Schedule(state.Dependency);
        state.Dependency = new StuckedParticlePresentJob { }.Schedule(state.Dependency);
    }
}