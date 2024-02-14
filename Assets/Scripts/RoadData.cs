using System.Collections;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Scriptable Objects/Road Data")]
public class RoadData : ScriptableObject
{
    public string roadName;
    public string[] axes;
    public GameObject prefab;

    public override string ToString() => roadName;
}
