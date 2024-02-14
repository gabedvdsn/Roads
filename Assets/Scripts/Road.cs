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
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Initialize(int _x, int _y)
    {
        x = _x;
        y = _y;
        gameObject.name = $"{data.roadName} ({x}, {y})";
    }

    public int[] GetPosition() => new[] { x, y };

    public RoadData GetData() => data;

    public bool CompareName(string _name) => data.roadName == _name;

    public override string ToString()
    {
        return gameObject.name;
    }
}
