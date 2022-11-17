using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;
using Unity.Collections;
using Unity.VisualScripting;

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