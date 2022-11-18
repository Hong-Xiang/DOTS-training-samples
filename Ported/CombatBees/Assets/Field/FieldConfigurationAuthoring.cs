using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Collections;

public class FieldConfigurationAuthoring : MonoBehaviour
{
    public float Gravity = -20f;
    public float3 Size = math.float3(100f, 20f, 30f);
    class FieldConfigurationBaker : Baker<FieldConfigurationAuthoring>
    {
        public override void Bake(FieldConfigurationAuthoring authoring)
        {
            AddComponent(new FieldConfiguration
            {
                Size = authoring.Size,
                Gravity = authoring.Gravity
            });
        }
    }
}
