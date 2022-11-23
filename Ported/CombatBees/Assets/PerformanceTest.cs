using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
partial struct MandelbrotNET
{
    [BurstCompile]
    public float Mandelbrot(uint width, uint height, uint iterations)
    {
        float data = 0.0f;

        for (uint i = 0; i < iterations; i++)
        {
            float
                left = -2.1f,
                right = 1.0f,
                top = -1.3f,
                bottom = 1.3f,
                deltaX = (right - left) / width,
                deltaY = (bottom - top) / height,
                coordinateX = left;

            for (uint x = 0; x < width; x++)
            {
                float coordinateY = top;

                for (uint y = 0; y < height; y++)
                {
                    float workX = 0;
                    float workY = 0;
                    int counter = 0;

                    while (counter < 255 && math.sqrt((workX * workX) + (workY * workY)) < 2.0f)
                    {
                        counter++;

                        float newX = (workX * workX) - (workY * workY) + coordinateX;

                        workY = 2 * workX * workY + coordinateY;
                        workX = newX;
                    }

                    data = workX + workY;
                    coordinateY += deltaY;
                }

                coordinateX += deltaX;
            }
        }

        return data;
    }
}

partial struct PerformanceDataResult : IComponentData
{
    public float Value;
}


[BurstCompile]
public partial struct TestPerformanceSystem : ISystem
{
    float accumulate;

    public void OnCreate(ref SystemState state)
    {
        accumulate = 0f;
        var e = state.EntityManager.CreateEntity(typeof(PerformanceDataResult));
        state.EntityManager.AddComponentData(e, new PerformanceDataResult { Value = 0f });
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var d in SystemAPI.Query<RefRW<PerformanceDataResult>>())
        {
            var s = new MandelbrotNET { };
            d.ValueRW.Value += s.Mandelbrot(128, 128, 8);
        }

    }
}

public partial struct TestPerformanceResultPrintSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var testD = SystemAPI.GetSingleton<PerformanceDataResult>();
        Debug.Log(testD.Value);
    }
}