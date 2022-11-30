using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;
using Unity.Collections;
using Unity.Rendering;

partial struct ParticleTag : IComponentData
{
}


partial struct ParticleSize : IComponentData
{
    public float3 size;
}

partial struct ParticleDuration : IComponentData
{
    public float Duration;
}

partial struct ParticleLife : IComponentData
{
    public float normalizedLife; // always within [0, 1]
}

partial struct StuckedParticle : IComponentData, IEnableableComponent
{
}

[BurstCompile]
struct StuckedParticleHelper
{
    public static void Prefab(ref SystemState state, in Entity prefabEntity)
    {
        state.EntityManager.AddComponent<StuckedParticle>(prefabEntity);
        state.EntityManager.SetComponentEnabled<StuckedParticle>(prefabEntity, false);
    }

    public static void MarkStucked(ref EntityCommandBuffer.ParallelWriter ecb, int sortKey, in Entity particleEntity)
    {
        ecb.SetComponentEnabled<StuckedParticle>(sortKey, particleEntity, true);
    }
}

partial struct BloodParticle : IComponentData
{
}

partial struct BeeSpawnParticle : IComponentData
{
}

partial struct ParticleSpawnData : IComponentData
{
    public Entity BloodPrefabEntity;
    public Entity BeeSpawnPrefabEntity;
}

partial struct ParticleCount : IComponentData
{
    public int TotalCount;
}

partial struct DuringSpawnParticleVariant : IComponentData, IEnableableComponent
{
    public float3 Position;
    public float3 Velocity;
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
partial class ParticleSimulationGroup : ComponentSystemGroup
{
}

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
partial struct ParticlePrefabInitializationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ParticleConfiguration>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    // [BurstCompile] // Can not use Burst compile since typeof is required
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ParticleConfiguration>();
        var bloodParticlePrefabEntity = config.particlePrefab;
        var entityManager = state.EntityManager;
        state.EntityManager.AddComponent<ParticleTag>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponent<ParticleSize>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponentData<ParticleLife>(bloodParticlePrefabEntity,
            new ParticleLife { normalizedLife = 1f });
        state.EntityManager.AddComponent<ParticleDuration>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponent<Velocity>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponent<URPMaterialPropertyBaseColor>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponentData(bloodParticlePrefabEntity,
            new PostTransformMatrix { Value = float4x4.Scale(0f) });
        state.EntityManager.AddComponent<RandomIndex>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponent<DuringSpawnParticleVariant>(bloodParticlePrefabEntity);
        state.EntityManager.SetComponentEnabled<DuringSpawnParticleVariant>(bloodParticlePrefabEntity, true);

        StuckedParticleHelper.Prefab(ref state, bloodParticlePrefabEntity);

        var sourceEntities = CollectionHelper.CreateNativeArray<Entity>(1, Allocator.Temp);
        var targetEntities = CollectionHelper.CreateNativeArray<Entity>(1, Allocator.Temp);
        sourceEntities[0] = bloodParticlePrefabEntity;
        state.EntityManager.CopyEntities(sourceEntities, targetEntities);

        var beeSpawnPrefabEntity = targetEntities[0];

        state.EntityManager.AddComponent<BloodParticle>(bloodParticlePrefabEntity);
        state.EntityManager.AddComponent<BeeSpawnParticle>(beeSpawnPrefabEntity);
        state.EntityManager.SetComponentData(beeSpawnPrefabEntity,
            new URPMaterialPropertyBaseColor { Value = math.float4(1f) });


        var particleSpawnData = state.EntityManager.CreateEntity(typeof(ParticleSpawnData));
        state.EntityManager.SetComponentData(particleSpawnData, new ParticleSpawnData
        {
            BloodPrefabEntity = bloodParticlePrefabEntity,
            BeeSpawnPrefabEntity = beeSpawnPrefabEntity
        });


        state.Enabled = false;
        sourceEntities.Dispose();
        targetEntities.Dispose();
    }
}

[BurstCompile]
partial struct ParticleSpawner
{
    // [ReadOnly] public ParticleConfiguration config;
    [ReadOnly] public ParticleSpawnData spawn;


    [BurstCompile]
    public void SpawnParticleSpawnFlash(ref Random random,
        EntityCommandBuffer.ParallelWriter ecb,
        int sortKey,
        float3 position,
        float3 velocity)
    {
        var instance = ecb.Instantiate(sortKey, spawn.BeeSpawnPrefabEntity);
        // ecb.AddComponent(sortKey, instance, new BeeSpawnParticle { });

        ecb.SetComponent(sortKey, instance, new LocalToWorldTransform
        {
            Value = UniformScaleTransform.FromPosition(position)
        });
        ecb.SetComponent(sortKey, instance, new Velocity { Value = velocity });
        ecb.SetComponent(sortKey, instance, new ParticleSize
        {
            size = math.float3(random.NextFloat(1f, 2f)),
        });
        ecb.SetComponent(sortKey, instance, new ParticleDuration
        {
            Duration = random.NextFloat(.25f, .5f)
        });
        ecb.SetComponent(sortKey, instance,
            new RandomIndex { random = Unity.Mathematics.Random.CreateFromIndex(random.NextUInt()) });
        // ecb.SetComponent(sortKey, instance, new URPMaterialPropertyBaseColor { Value = math.float4(1f) });

        ecb.SetComponentEnabled<DuringSpawnParticleVariant>(sortKey, instance, false);
    }

    [BurstCompile]
    public void SpawnParticleBlood(ref Random random,
        in int sortKey,
        EntityCommandBuffer.ParallelWriter ecb,
        float3 position,
        float3 velocity,
        int count,
        float3 positionVariant,
        float3 velocityVariant)
    {
        if (count == 0)
        {
            return;
        }

        var instances = CollectionHelper.CreateNativeArray<Entity>(count, Allocator.Temp);
        ecb.Instantiate(sortKey, spawn.BloodPrefabEntity, instances);

        // for (var i = 0; i < count; i++)
        foreach (var instance in instances)
        {
            // var instance = ecb.Instantiate(sortKey, spawn.BloodPrefabEntity);
            // ecb.AddComponent(sortKey, instance, new BloodParticle { });

            ecb.SetComponent(sortKey, instance, new LocalToWorldTransform
            {
                Value = UniformScaleTransform.FromPosition(position)
            });
            ecb.SetComponent(sortKey, instance, new Velocity
            {
                Value = velocity
            });
            ecb.SetComponent(sortKey, instance,
                new RandomIndex { random = Unity.Mathematics.Random.CreateFromIndex(random.NextUInt()) });
            ecb.SetComponent(sortKey, instance, new DuringSpawnParticleVariant
            {
                Position = positionVariant,
                Velocity = velocityVariant
            });
        }

        instances.Dispose();
    }
}


[BurstCompile]
partial struct ParticleGenerateRandomValueJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ECB;

    [BurstCompile]
    void Execute(ref TransformAspect transform,
        ref Velocity velocity,
        ref RandomIndex random,
        ref ParticleSize size,
        ref ParticleDuration particleDuration,
        ref URPMaterialPropertyBaseColor baseColor,
        in DuringSpawnParticleVariant variant,
        in Entity entity,
        [EntityInQueryIndex] int inQueryIndex)
    {
        transform.Position += random.random.NextFloat3(variant.Position) - variant.Position;
        velocity.Value += random.random.NextFloat3Direction() * variant.Velocity;
        size.size = math.float3(1f) * random.random.NextFloat(.1f, .2f);
        particleDuration.Duration = random.random.NextFloat(3f, 5f);
        // particleLife.Duration =  5f;
        var hsv = random.random.NextFloat3(
            math.float3(-.05f, .75f, .3f),
            math.float3(.05f, 1f, .8f));
        var rgb = UnityEngine.Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
        baseColor.Value = math.float4(rgb.r, rgb.g, rgb.b, 1f);
        ECB.SetComponentEnabled<DuringSpawnParticleVariant>(inQueryIndex, entity, false);
        // ECB.RemoveComponent<SpawningBloodParticleVariant>(inQueryIndex, entity);
    }
}

[UpdateInGroup(typeof(ParticleSimulationGroup))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct ParticleDuringSpawnRandomValueSystem : ISystem
{
    EntityQuery SpawningBloodParticle;

    public void OnCreate(ref SystemState state)
    {
        SpawningBloodParticle = state.GetEntityQuery(typeof(DuringSpawnParticleVariant));
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();
        state.Dependency = new ParticleGenerateRandomValueJob { ECB = ecb }.ScheduleParallel(state.Dependency);
    }
}


[UpdateInGroup(typeof(ParticleSimulationGroup))]
[RequireMatchingQueriesForUpdate]
[BurstCompile]
partial struct ParticleSpawnSystem : ISystem
{
    EntityQuery ParticleQuery;

    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
        ParticleQuery = state.GetEntityQuery(typeof(ParticleTag));

        state.RequireForUpdate<ParticleSpawnData>();
        state.RequireForUpdate<ParticleConfiguration>();
        state.RequireForUpdate<ParticleCount>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        var spawn = SystemAPI.GetSingleton<ParticleSpawnData>();
        var config = SystemAPI.GetSingleton<ParticleConfiguration>();
        var ps = new ParticleSpawner
        {
            spawn = spawn
        };
        if (ParticleQuery.CalculateEntityCount() > config.maxParticleCount)
        {
            return;
        }

        var random = Random.CreateFromIndex(42);

        ps.SpawnParticleBlood(ref random, 0, ecb.AsParallelWriter(), float3.zero, float3.zero, 100,
            float3.zero, 6f);

        state.Enabled = false;
    }
}

[WithNone(typeof(StuckedParticle))]
[BurstCompile]
partial struct ParticleSimulationJob : IJobEntity
{
    [ReadOnly] public FieldComponent field;
    public EntityCommandBuffer.ParallelWriter ECB;
    public float deltaTime;

    [BurstCompile]
    void Execute(ref TransformAspect transform, ref Velocity velocity, ref ParticleSize particle, in Entity entity,
        [EntityInQueryIndex] int inQueryIndex)
    {
        velocity.Value += math.float3(0f, 1f, 0f) * (field.Gravity * deltaTime);
        var position = transform.Position + velocity.Value * deltaTime;

        var stucked = math.abs(position) > (field.Size * .5f);

        position = math.select(
            position,
            field.Size * .5f * math.sign(position),
            stucked
        );
        transform.Position = position;

        var splat = math.select(math.float3(1f), math.abs(velocity.Value * .3f) + 1f, stucked);
        particle.size *= math.float3(
            splat.y * splat.z,
            splat.x * splat.z,
            splat.x * splat.y
        );

        if (math.any(stucked))
        {
            // ECB.AddComponent(inQueryIndex, entity, new StuckedParticle { });
            // ECB.SetComponentEnabled<StuckedParticle>(inQueryIndex, entity, true);
            StuckedParticleHelper.MarkStucked(ref ECB, inQueryIndex, entity);
            velocity.Value = float3.zero;
        }
    }
}


[UpdateInGroup(typeof(ParticleSimulationGroup))]
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
[WithNone(typeof(DuringSpawnParticleVariant))]
partial struct ParticleRemoveJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ECB;
    public float DeltaTime;

    [BurstCompile]
    void Execute(ref ParticleLife life, in ParticleDuration duration, in Entity entity,
        [EntityInQueryIndex] int inQueryIndex)
    {
        life.normalizedLife -= DeltaTime / duration.Duration;
        if (life.normalizedLife < 0f)
        {
            ECB.DestroyEntity(inQueryIndex, entity);
        }
    }
}

[UpdateInGroup(typeof(ParticleSimulationGroup))]
[BurstCompile]
[UpdateAfter(typeof(ParticleSimulationSystem))]
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
        var ECB = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>()
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
        in ParticleSize particle,
        in Velocity velocity,
        in ParticleLife life)
    {
        var scale = math.float3(particle.size * life.normalizedLife);
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
[WithAll(typeof(BeeSpawnParticle))]
[WithNone(typeof(StuckedParticle))]
partial struct SpawnParticlePresentJob : IJobEntity
{
    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
        ref URPMaterialPropertyBaseColor color,
        in ParticleSize particle,
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
        in ParticleSize particle,
        in ParticleLife life)
    {
        matrix.Value = float4x4.Scale(particle.size * life.normalizedLife);
        color.Value.w = life.normalizedLife;
    }
}

// TODO: 处理Z方向scale不稳定的问题
[BurstCompile]
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(PresentationSystemGroup))]
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