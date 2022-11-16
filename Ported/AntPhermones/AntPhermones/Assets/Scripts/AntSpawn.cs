using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

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
