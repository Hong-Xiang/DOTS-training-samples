using Unity.Entities;
using UnityEngine;
class ParticleConfigurationAuthoring : MonoBehaviour
{
    public float speedStretch;
    public int maxParticleCount = 10000;
    public int beeDeathParticleCount;
    public int beeAttackParticleCount;
    public GameObject particlePrefab;

    class ParticleConfigurationBaker : Baker<ParticleConfigurationAuthoring>
    {
        public override void Bake(ParticleConfigurationAuthoring authoring)
        {
            AddComponent<ParticleConfiguration>(new ParticleConfiguration
            {
                speedStretch = authoring.speedStretch,
                maxParticleCount = authoring.maxParticleCount,
                particlePrefab = GetEntity(authoring.particlePrefab),
                beeAttackParticleCount = authoring.beeAttackParticleCount,
                beeDeathParticleCount = authoring.beeDeathParticleCount
            });
        }
    }
}

partial struct ParticleConfiguration : IComponentData
{
    public float speedStretch;
    public int maxParticleCount;
    public Entity particlePrefab;
    public int beeDeathParticleCount;
    public int beeAttackParticleCount;
}