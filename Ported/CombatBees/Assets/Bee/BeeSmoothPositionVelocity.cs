using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Rendering;
using Unity.Jobs;

struct Attacking : IComponentData
{
    public bool isAttacking;
}

struct SmoothPosition : IComponentData
{
    public float3 Position;
}

struct SmoothVelocity : IComponentData
{
    public float3 Velocity;
}

[BurstCompile]
readonly partial struct SmoothPositionVelocityAspect : IAspect
{
    readonly RefRW<SmoothPosition> position;
    readonly RefRW<SmoothVelocity> velocity;
    readonly TransformAspect transform;
    public float3 Position
    {
        get => position.ValueRO.Position;
        set => position.ValueRW.Position = value;
    }
    public float3 Velocity
    {
        get => velocity.ValueRO.Velocity;
        set => velocity.ValueRW.Velocity = value;
    }

    [BurstCompile]
    public void UpdateNormal(float deltaTime, float rotationStiffness)
    {
        var oldPosition = Position;
        var newPosition = math.lerp(oldPosition, transform.Position, deltaTime * rotationStiffness);
        Position = newPosition;
        Velocity = newPosition - oldPosition;

    }

    [BurstCompile]
    public void UpdateAttacking()
    {
        var oldPosition = Position;
        var newPosition = transform.Position;
        Position = newPosition;
        Velocity = newPosition - oldPosition;

    }

    [BurstCompile]
    public static void AddSmoothPositionVelocity(ref EntityCommandBuffer ecb, in Entity self)
    {
        ecb.AddComponent(self, new SmoothPosition { });
        ecb.AddComponent(self, new SmoothVelocity { });
    }
}

[BurstCompile]
[WithAll(typeof(BeeTag))]
partial struct BeeSmoothRotationJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration config;
    public float deltaTime;

    [BurstCompile]
    void Execute(ref SmoothPositionVelocityAspect smoothAspect, in Attacking attacking)
    {
        if (attacking.isAttacking)
        {
            smoothAspect.UpdateAttacking();
        }
        else
        {
            smoothAspect.UpdateNormal(deltaTime, config.rotationStiffness);
        }
    }
}

[BurstCompile]
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeeMoveSystem))]
partial struct BeeSmoothRotationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new BeeSmoothRotationJob
        {
            config = config,
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[WithNone(typeof(Dying))]
[BurstCompile]
partial struct AliveBeeSmoothMovePresentJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration config;

    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
                 in BeeSize beeSize,
                 in Velocity velocity,
                 in SmoothPositionVelocityAspect smooth
        )
    {
        float3 scale = math.float3(beeSize.size);
        float stretch = math.max(1f, math.length(velocity.Value) * config.speedStretch);
        scale.z *= stretch;
        scale.x /= (stretch - 1f) / 5f + 1f;
        scale.y /= (stretch - 1f) / 5f + 1f;

        quaternion rotation = quaternion.identity;
        if (math.length(smooth.Velocity) > math.EPSILON)
        {
            rotation = quaternion.LookRotation(smooth.Velocity, math.float3(0f, 1f, 0f));
        }
        matrix.Value = float4x4.TRS(float3.zero, rotation, scale);
    }
}

[BurstCompile]
partial struct DyingBeeSmoothMovePresentJob : IJobEntity
{
    [ReadOnly] public BeeConfiguration config;

    [BurstCompile]
    void Execute(ref PostTransformMatrix matrix,
                 ref URPMaterialPropertyBaseColor color,
                 in BeeTag bee,
                 in Team team,
                 in Dying dying,
                 in Velocity velocity,
                 in SmoothPositionVelocityAspect smooth
        )
    {

        quaternion rotation = quaternion.identity;
        if (math.length(smooth.Velocity) > math.EPSILON)
        {
            rotation = quaternion.LookRotation(smooth.Velocity, math.float3(0f, 1f, 0f));
        }
        matrix.Value = float4x4.TRS(float3.zero, rotation, math.float3(math.sqrt(dying.Timer)));
        var c = team.Value == 0 ? config.teamAColor : config.teamBColor;
        color.Value = math.float4(math.float3(c.r, c.g, c.b) * .75f, 1f);
    }
}

[BurstCompile]
[RequireMatchingQueriesForUpdate]
partial struct BeeSmoothMovePresentSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeeConfiguration>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<BeeConfiguration>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        state.Dependency = JobHandle.CombineDependencies(
            new AliveBeeSmoothMovePresentJob
            {
                config = config,
            }.ScheduleParallel(state.Dependency),
            new DyingBeeSmoothMovePresentJob
            {
                config = config
            }.ScheduleParallel(state.Dependency)
        );
    }
}