using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class Configuration : MonoBehaviour
{
    public int antCount;
    public int mapSize = 128;
    public int bucketResolution;
    public Vector3 antSize;
    public float antSpeed;
    [Range(0f, 1f)]
    public float antAccel;
    public float trailAddSpeed;
    [Range(0f, 1f)]
    public float trailDecay;
    public float randomSteering;
    public float pheromoneSteerStrength;
    public float wallSteerStrength;
    public float goalSteerStrength;
    public float outwardStrength;
    public float inwardStrength;
    public int rotationResolution = 360;
    public int obstacleRingCount;
    [Range(0f, 1f)]
    public float obstaclesPerRing;
    public float obstacleRadius;

    public GameObject ObstaclePrefab;
    public GameObject ColonyPrefab;
    public GameObject AntPrefab;
    public GameObject ResourcePrefab;

    class ConfigurationBaker : Baker<Configuration>
    {
        public override void Bake(Configuration authoring)
        {
            AddComponent(new ConfigurationComponent
            {
                antCount = authoring.antCount,
                mapSize = authoring.mapSize,
                bucketResolution = authoring.bucketResolution,
                antSize = authoring.antSize,
                antSpeed = authoring.antSpeed,
                antAccel = authoring.antAccel,
                trailAddSpeed = authoring.trailAddSpeed,
                trailDecay = authoring.trailDecay,
                randomSteering = authoring.randomSteering,
                pheromoneSteerStrength = authoring.pheromoneSteerStrength,
                wallSteerStrength = authoring.wallSteerStrength,
                goalSteerStrength = authoring.goalSteerStrength,
                outwardStrength = authoring.outwardStrength,
                inwardStrength = authoring.inwardStrength,
                rotationResolution = authoring.rotationResolution,
                obstacleRingCount = authoring.obstacleRingCount,
                obstaclesPerRing = authoring.obstaclesPerRing,
                obstacleRadius = authoring.obstacleRadius,
                ObstaclePrefab = GetEntity(authoring.ObstaclePrefab),
                ColonyPrefab = GetEntity(authoring.ColonyPrefab),
                AntPrefab = GetEntity(authoring.AntPrefab),
                ResourcePrefab = GetEntity(authoring.ResourcePrefab),
            });
        }
    }
}


partial struct ConfigurationComponent : IComponentData
{
    public int antCount;
    public int mapSize;
    public int bucketResolution;
    public float3 antSize;
    public float antSpeed;
    public float antAccel;
    public float trailAddSpeed;
    public float trailDecay;
    public float randomSteering;
    public float pheromoneSteerStrength;
    public float wallSteerStrength;
    public float goalSteerStrength;
    public float outwardStrength;
    public float inwardStrength;
    public int rotationResolution;
    public int obstacleRingCount;
    public float obstaclesPerRing;
    public float obstacleRadius;

    public Entity ObstaclePrefab;
    public Entity ColonyPrefab;
    public Entity AntPrefab;
    public Entity ResourcePrefab;
}


