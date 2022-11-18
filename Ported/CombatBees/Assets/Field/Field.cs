using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Collections;


partial struct FieldConfiguration : IComponentData
{
    public float3 Size;
    public float Gravity;
}