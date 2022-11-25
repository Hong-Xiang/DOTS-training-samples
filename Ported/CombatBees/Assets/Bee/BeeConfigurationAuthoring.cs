using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

public class BeeConfigurationAuthoring : MonoBehaviour
{
    public GameObject BeePrefab;
    public Color[] teamColors;
    public float minBeeSize;
    public float maxBeeSize;
    public float speedStretch;
    public float rotationStiffness;
    public float aggression;
    public float flightJitter;
    public float teamAttraction;
    public float teamRepulsion;
    public float damping;
    public float chaseForce;
    public float carryForce;
    public float grabDistance;
    public float attackDistance;
    public float attackForce;
    public float hitDistance;
    public float maxSpawnSpeed;
    public int startBeeCount;

    class BeeConfigurationBaker : Baker<BeeConfigurationAuthoring>
    {
        public override void Bake(BeeConfigurationAuthoring authoring)
        {
            AddComponent<BeeConfiguration>(new BeeConfiguration
            {
                BeePrefab = GetEntity(authoring.BeePrefab),
                teamAColor = authoring.teamColors[0],
                teamBColor = authoring.teamColors[1],
                minBeeSize = authoring.minBeeSize,
                maxBeeSize = authoring.maxBeeSize,
                speedStretch = authoring.speedStretch,
                rotationStiffness = authoring.rotationStiffness,
                aggression = authoring.aggression,
                flightJitter = authoring.flightJitter,
                teamAttraction = authoring.teamAttraction,
                teamRepulsion = authoring.teamRepulsion,
                damping = authoring.damping,
                chaseForce = authoring.chaseForce,
                carryForce = authoring.carryForce,
                grabDistance = authoring.grabDistance,
                attackDistance = authoring.attackDistance,
                attackForce = authoring.attackForce,
                hitDistance = authoring.hitDistance,
                maxSpawnSpeed = authoring.maxSpawnSpeed,
                startBeeCount = authoring.startBeeCount
            });
        }
    }
}

public struct BeeConfiguration : IComponentData
{
    public Entity BeePrefab;
    public Color teamAColor;
    public Color teamBColor;
    public float minBeeSize;
    public float maxBeeSize;
    public float speedStretch;
    public float rotationStiffness;
    public float aggression;
    public float flightJitter;
    public float teamAttraction;
    public float teamRepulsion;
    public float damping;
    public float chaseForce;
    public float carryForce;
    public float grabDistance;
    public float attackDistance;
    public float attackForce;
    public float hitDistance;
    public float maxSpawnSpeed;
    public int startBeeCount;
}

