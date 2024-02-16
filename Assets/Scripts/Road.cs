using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using UnityEngine;

public class Road : MonoBehaviour
{
    [SerializeField] private RoadData data;

    private PassableRoadPacket generationPacket;

    public void Initialize(PassableRoadPacket _generationPacket)
    {
        generationPacket = _generationPacket;
        
        gameObject.name = $"{data.roadName} ({generationPacket.x}, {generationPacket.x}) => {generationPacket.generationAxis}";
    }

    public int[] GetPosition() => new[] { generationPacket.x, generationPacket.y };

    public string GetGenerationAxis() => generationPacket.generationAxis;

    public bool RequiresCorners() => generationPacket.requiresCorners;

    public RoadData GetData() => data;
    
    public override string ToString() => gameObject.name;
    
}
