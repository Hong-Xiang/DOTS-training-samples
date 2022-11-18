using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Collections;

public class ResourceConfigurationAuthoring : MonoBehaviour
{
    public GameObject resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate = .1f;
    public int beesPerResource;
    [Space(10)]
    public int startResourceCount;

    class ResourceConfigurationBaker : Baker<ResourceConfigurationAuthoring>
    {
        public override void Bake(ResourceConfigurationAuthoring authoring)
        {
            AddComponent(new ResourceConfiguration
            {
                resourcePrefab = GetEntity(authoring.resourcePrefab),
                resourceSize = authoring.resourceSize,
                snapStiffness = authoring.snapStiffness,
                carryStiffness = authoring.carryStiffness,
                spawnRate = authoring.spawnRate,
                beesPerResource = authoring.beesPerResource,
                startResourceCount = authoring.startResourceCount
            });
        }
    }
}