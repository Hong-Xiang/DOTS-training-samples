using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using System;

public class ResourceManager : MonoBehaviour
{
    public GameObject resourcePrefab;
    public float resourceSize;
    public float snapStiffness;
    public float carryStiffness;
    public float spawnRate = .1f;
    public int beesPerResource;
    [Space(10)]
    public int startResourceCount;
    public int maxResourceCount = 1000;

    class ResourceManagerBaker : Baker<ResourceManager>
    {
        public override void Bake(ResourceManager authoring)
        {
            AddComponent(new ResourceConfiguration
            {
                resourcePrefab = GetEntity(authoring.resourcePrefab),
                resourceSize = authoring.resourceSize,
                snapStiffness = authoring.snapStiffness,
                carryStiffness = authoring.carryStiffness,
                spawnRate = authoring.spawnRate,
                beesPerResource = authoring.beesPerResource,
                startResourceCount = authoring.startResourceCount,
                maxResourceCount = authoring.maxResourceCount
            });
        }
    }
}