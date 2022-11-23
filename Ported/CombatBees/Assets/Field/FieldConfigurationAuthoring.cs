using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;

class FieldConfigurationAuthoring : MonoBehaviour
{
    public float3 size = math.float3(100f, 20f, 30f);
    public float gravity = -20f;

    class FieldConfigurationBaker : Baker<FieldConfigurationAuthoring>
    {
        public override void Bake(FieldConfigurationAuthoring authoring)
        {
            AddComponent<FieldComponent>(new FieldComponent
            {
                Gravity = authoring.gravity,
                Size = authoring.size
            });
        }
    }
}

struct FieldComponent : IComponentData
{
    public float Gravity;
    public float3 Size;

    public float3 TargetPosition(float3 currentBeePosition, int team)
    {
        return math.float3(-Size.x * .45f + Size.x * .9f * team, 0f, currentBeePosition.z);
    }
}
