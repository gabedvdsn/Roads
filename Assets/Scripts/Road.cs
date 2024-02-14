using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using UnityEngine;

public class Road : MonoBehaviour
{
    [SerializeField] private RoadData data;

    private int x, y;

    public void Initialize(int _x, int _y)
    {
        x = _x;
        y = _y;
        gameObject.name = $"{data.roadName} ({x}, {y})";
    }

    public int[] GetPosition() => new[] { x, y };

    public RoadData GetData() => data;
    
    public override string ToString() => gameObject.name;
    
}
