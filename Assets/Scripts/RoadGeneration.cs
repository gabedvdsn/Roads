using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class RoadGeneration : MonoBehaviour
{
    
    [Header("Simulation Info")] 
    // If neither bool is true, use both square and diagonal roads
    [SerializeField] private bool useOnlySquareRoads;
    [SerializeField] private bool useOnlyDiagonalRoads;
    
    [Header("Road Info")]
    [SerializeField] private List<RoadData> squareRoadDatas;
    [Space]
    [SerializeField] private List<RoadData> diagonalRoadDatas;
    [SerializeField] private List<RoadData> diagonalCornerRoadDatas;

    private List<RoadData> allRoadDatas;
    private string[] validAxes;
    private string[] validTerminals;
    
    private int generationDepth;
    private float generationMagnitude;

    [Header("UI Info")] 
    [SerializeField] private TMPro.TMP_InputField generationDepthField;
    [SerializeField] private TMPro.TMP_InputField generationMagnitudeField;

    private RoadSystem roadSystem;
    private Dictionary<RoadData, Dictionary<string, List<RoadData>>> connectionsByAxis;

    private readonly string[] squareTerminals =
    {
        "Up",
        "Down",
        "Left",
        "Right"
    };
    
    private readonly string[] diagonalTerminals =
    {
        "UpRight",
        "UpLeft",
        "DownRight",
        "DownLeft"
    };

    // Separate array of square axes
    private readonly string[] squareAxes =
    {
        "up",
        "down",
        "left",
        "right"
    };
    
    // Separate array of diagonal axes for corners
    private readonly string[] diagonalAxes =
    {
        "upright",
        "upleft",
        "downright",
        "downleft"
    };
    
    // Start is called before the first frame update
    void Start()
    {
        roadSystem = new RoadSystem();
        
        CreateVariablesBySettings();
        CreateConnectionsByAxis();
    }

    #region Setup
    
    void CreateVariablesBySettings()
    {
        allRoadDatas = new List<RoadData>();

        if (!useOnlySquareRoads && !useOnlyDiagonalRoads)
        {
            allRoadDatas = squareRoadDatas.Concat(diagonalRoadDatas).ToList();
            validAxes = squareAxes.Concat(diagonalAxes).ToArray();
            validTerminals = squareTerminals.Concat(diagonalTerminals).ToArray();
        }
        else if (useOnlySquareRoads && !useOnlyDiagonalRoads)
        {
            allRoadDatas = squareRoadDatas;
            validAxes = squareAxes;
            validTerminals = squareTerminals;
        }
        else if (!useOnlySquareRoads && useOnlyDiagonalRoads)
        {
            allRoadDatas = diagonalRoadDatas;
            validAxes = diagonalAxes;
            validTerminals = diagonalTerminals;
        }
        
        // In the case that both flags are true, allRoadDatas remains empty
    }
    
    void CreateConnectionsByAxis()
    {
        connectionsByAxis = new Dictionary<RoadData, Dictionary<string, List<RoadData>>>();
        
        foreach (RoadData thisRoadData in allRoadDatas)
        {
            connectionsByAxis[thisRoadData] = new Dictionary<string, List<RoadData>>();
            
            foreach (string thisAxis in thisRoadData.axes)
            {
                connectionsByAxis[thisRoadData][thisAxis] = new List<RoadData>();

                foreach (var otherRoadData in allRoadDatas.Where(otherRoadData => otherRoadData.axes.Contains(GetOpposingAxis(thisAxis))))
                {
                    connectionsByAxis[thisRoadData][thisAxis].Add(otherRoadData);
                }
            }
        }
    }
    
    #endregion

    #region Simulation
    
    void DoSimulation()
    {
        GetGenerationDepthFromField();
        GetGenerationMagnitude();
        
        GenerateStart();
        
        Debug.Log($"Generation Depth => {generationDepth * generationMagnitude}");
        
        // Do generation
        for (int _ = 0; _ < generationDepth; ++_) GenerateRoads();
        
        // Close all remaining non-terminal roads with terminal roads
        TerminateAllRoads();
        
        Debug.Log($"Roads in system: => {roadSystem.GetSize()}");
    }
    
    void GenerateStart()
    {
        RoadData roadData = GetTerminalForAxis(validAxes[Random.Range(0, validAxes.Length)]);

        if (roadData is null)
        {
            Debug.Log($"Could not generate start because initial RoadData is null");
            return;
        }
        
        GameObject go = Instantiate(roadData.prefab, transform);
        
        Road startRoad = go.GetComponent<Road>();
        startRoad.Initialize(0, 0);
        
        roadSystem.AddAt(go.GetComponent<Road>(), 0, 0);

        // Generate off of startRoad manually
        List<RoadPacket> validConnections = GetValidConnectionRoadsFor(go.GetComponent<Road>());
        RoadPacket packet = GetSomeNonTerminalRoadPacket(validConnections);

        InstantiateRoad(startRoad, packet);

    }

    void GenerateRoads()
    {
        foreach (Road _road in roadSystem.GetAllFreeNonTerminalRoads(this))
        {
            if (roadSystem.GetSize() >= generationDepth * generationMagnitude) return;
            
            List<RoadPacket> validConnections = GetValidConnectionRoadsFor(_road);
            
            if (validConnections.Count == 0) continue;  // exceedingly rare case when neighboring roads on all axes need to connect (just haven't made this art yet lol)

            InstantiateRoad(_road, GetSomeNonTerminalRoadPacket(validConnections));
        }
    }
    
    void TerminateAllRoads()
    {
        bool reTerminate = false;
        
        foreach (Road _road in roadSystem.GetAllFreeNonTerminalRoads(this))
        {
            int[] position = _road.GetPosition();
            
            foreach (string axis in _road.GetData().axes)
            {
                int[] offsetPosition =
                {
                    position[0] + AxisToOffset(axis, 'x'),
                    position[1] + AxisToOffset(axis, 'y')
                };
                
                // If road exists at offset position, continue
                if (roadSystem.RoadExistsAt(offsetPosition[0], offsetPosition[1])) continue;
                
                RoadData terminalData = GetTerminalForAxis(axis);
                
                // Check if terminal is valid there
                if (PotentialRoadIsValidAtPosition(terminalData, offsetPosition[0], offsetPosition[1]))
                {
                    // The terminal is valid, let's create it
                    RoadPacket packet = new RoadPacket(offsetPosition[0], offsetPosition[1], axis, GetTerminalForAxis(axis));
                    InstantiateRoad(_road, packet);
                }
                else
                {
                    // The terminal is invalid, create any valid road instead
                    List<RoadPacket> validConnectionRoads = GetValidConnectionRoadsFor(_road);

                    if (validConnectionRoads.Count == 0) continue;  // exceedingly rare case when neighboring roads on all axes need to connect
                    
                    InstantiateRoad(_road, validConnectionRoads[Random.Range(0, validConnectionRoads.Count)]);
                    
                    // Non-terminal is created
                    reTerminate = true;
                }
            }
        }
        
        if (reTerminate) TerminateAllRoads();
    }
    
    void InstantiateRoad(Road currRoad, RoadPacket roadPacket)
    {
        GameObject go = Instantiate(roadPacket.roadData.prefab, 
            GetConnectionPosOnAxis(currRoad, roadPacket.roadData.prefab, roadPacket.axis), 
            Quaternion.identity, 
            transform);
        
        go.GetComponent<Road>().Initialize(roadPacket.x, roadPacket.y);
        
        roadSystem.AddAt(go.GetComponent<Road>(), roadPacket.x, roadPacket.y);
        
        if (roadPacket.requiresCorners) InstantiateCornerRoads();
    }
    
    void InstantiateCornerRoads() { }
    
    void DestroyAllRoadsInSystem()
    {
        // foreach (Road _road in roadSystem.GetAllFreeNonTerminalRoads(this, includeTerminals: true)) Destroy(_road);
        for (int i = 0; i < transform.childCount; ++i) Destroy(transform.GetChild(i).gameObject);
    }

    #endregion
    
    #region Calculation
    
    RoadData FindRoad(string _name) => allRoadDatas.FirstOrDefault(data => data.roadName == _name);

    List<RoadPacket> GetValidConnectionRoadsFor(Road _road)
    {
        // Returns a list of RoadPackets where each RoadPacket represents the position, axis,
        // and data of new road to create off of _road
        
        RoadData roadData = _road.GetData();
        int[] roadPosition = _road.GetPosition();

        List<RoadPacket> validConnectionRoads = new List<RoadPacket>();

        foreach (string axis in connectionsByAxis[roadData].Keys)
        {
            foreach (RoadData potentialRoadData in connectionsByAxis[roadData][axis])
            {
                int _x = roadPosition[0] + AxisToOffset(axis, 'x');
                int _y = roadPosition[1] + AxisToOffset(axis, 'y');
                
                if (roadSystem.RoadExistsAt(_x, _y)) continue;
                
                if (PotentialRoadIsValidAtPosition(potentialRoadData, _x, _y))
                {
                    validConnectionRoads.Add(new RoadPacket(_x, _y, axis, potentialRoadData));
                }
            }
        }

        return validConnectionRoads;

    }

    bool PotentialRoadIsValidAtPosition(RoadData potentialRoad, int x, int y)
    {
        // Confirm potentialRoadData at (x, y) has valid connections to all neighboring roads, if they exist
        foreach (string axis in validAxes)
        {
            int[] offsetPosition =
            {
                x + AxisToOffset(axis, 'x'),
                y + AxisToOffset(axis, 'y')
            };
            
            // If no road exists, continue
            if (!roadSystem.GetAt(offsetPosition[0], offsetPosition[1], out Road existingRoad)) continue;
            
            // Check if existingRoad and potentialRoad run parallel (neither is seeking a connection), if so continue 
            if (!connectionsByAxis[existingRoad.GetData()].ContainsKey(GetOpposingAxis(axis)) &&
                !connectionsByAxis[potentialRoad].ContainsKey(axis)) continue;
            
            // Check if existingRoad connects to potentialRoad along opposing axis, if not, return false
            if (!(connectionsByAxis[existingRoad.GetData()].ContainsKey(GetOpposingAxis(axis)) && potentialRoad.axes.Contains(axis))) return false;
        }

        // we have verified if a neighboring road exists, and if it does, that it connects to potentialRoad
        return true;
    }

    string GetOpposingAxis(string axis)
    {
        return axis switch
        {
            "up" => "down",
            "down" => "up",
            "left" => "right",
            "right" => "left",
            "upleft" => "downright",
            "upright" => "downleft",
            "downleft" => "upright",
            "downright" => "upleft",
            _ => ""
        };
    }

    public int AxisToOffset(string axis, char direction)
    {
        if (!validAxes.Contains(axis)) Debug.Log($"Axis => {axis} does not exist!");
        return direction switch
        {
            'x' => axis switch
            {
                "up" => 0,
                "down" => 0,
                "left" => -1,
                "right" => 1,
                "upleft" => -1,
                "upright" => 1,
                "downleft" => -1,
                "downright" => 1,
                _ => 0
            },
            'y' => axis switch
            {
                "up" => 1,
                "down" => -1,
                "left" => 0,
                "right" => 0,
                "upleft" => 1,
                "upright" => -1,
                "downleft" => 1,
                "downright" => -1,
                _ => 0
            },
            _ => 0
        };
    }

    public bool RoadIsTerminal(RoadData _roadData) => validTerminals.Contains(_roadData.roadName);
    
    RoadPacket GetSomeNonTerminalRoadPacket(List<RoadPacket> validConnections)
    {
        RoadPacket packet = validConnections[Random.Range(0, validConnections.Count)];
        
        int attempts = 0;
                
        while (RoadIsTerminal(packet.roadData))
        {
            if (attempts >= validTerminals.Length) break;
                    
            packet = validConnections[Random.Range(0, validConnections.Count)];
            attempts += 1;
        }
        
        return packet;
    }

    RoadData GetTerminalForAxis(string axis) => FindRoad(GetOpposingAxis(axis).FirstCharacterToUpper());

    Vector3 GetConnectionPosOnAxis(Road currRoad, GameObject nextPrefab, string axis)
    {
        Vector3 currPosition = currRoad.transform.position;
        
        Sprite thisSprite = currRoad.GetData().prefab.GetComponent<SpriteRenderer>().sprite;
        Sprite nextSprite = nextPrefab.GetComponent<SpriteRenderer>().sprite;

        return axis switch
        {
            "left" => GetLeftConnectionPos(currPosition, thisSprite, nextSprite),
            "right" => GetRightConnectionPos(currPosition, thisSprite, nextSprite),
            "up" => GetUpConnectionPos(currPosition, thisSprite, nextSprite),
            "down" => GetDownConnectionPos(currPosition, thisSprite, nextSprite),
            _ => Vector3.zero
        };
    }

    Vector3 GetLeftConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            -(thisSprite.textureRect.width / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2),
            transform.position.y,
            0);
    }
    
    Vector3 GetRightConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            thisSprite.textureRect.width / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2,
            transform.position.y,
            0);
    }
    
    Vector3 GetUpConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            transform.position.x,
            thisSprite.textureRect.height / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.height / otherSprite.pixelsPerUnit / 2,
            0);
    }
    
    Vector3 GetDownConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            transform.position.x,
            -(thisSprite.textureRect.height / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.height / otherSprite.pixelsPerUnit / 2),
            0);
    }
    
    #endregion
    
    #region Editor
    
    public void OnClickReset()
    {
        DestroyAllRoadsInSystem();
        
        roadSystem.Reset();
        
        DoSimulation();
    }

    public void GetGenerationDepthFromField() => int.TryParse(generationDepthField.text, out generationDepth);
    
    public void GetGenerationMagnitude() => float.TryParse(generationMagnitudeField.text, out generationMagnitude);

    private void OnValidate()
    {
        if (useOnlySquareRoads && useOnlyDiagonalRoads)
            throw new Exception("Cannot use only square roads and only diagonal roads at once. Either leave both as 'false' or mark one as true");
    }
    #endregion
}

public class RoadSystem
{
    private Dictionary<int, Dictionary<int, Road>> system;
    private int roadsInSystem;

    public RoadSystem()
    {
        this.system = new Dictionary<int, Dictionary<int, Road>>();
        roadsInSystem = 0;
    }

    public int GetSize() => roadsInSystem;

    public void AddAt(Road road, int x, int y)
    {
        if (system.ContainsKey(x))
        {
            if (system[x] is null)
            {
                system[x] = new Dictionary<int, Road>() { { y, road } }; // y level not initialized, initialize y level
                roadsInSystem += 1;
            }
            else if (!system[x].ContainsKey(y))
            {
                system[x][y] = road; // y level initialized, but [x][y] does not exist, add
                roadsInSystem += 1;
            }
        }
        else
        {
            system[x] = new Dictionary<int, Road>() { { y, road } };  // x level does not exist, initialize y level and add
            roadsInSystem += 1;
        }
    }

    public bool RoadExistsAt(int x, int y)
    {
        if (system.ContainsKey(x)) return system[x] is not null && system[x].ContainsKey(y);
        
        return false;
    }

    public bool GetAt(int x, int y, out Road _road)
    {
        _road = RoadExistsAt(x, y) ? system[x][y] : null;

        return _road is not null;
    }

    public List<Road> GetAllFreeNonTerminalRoads(RoadGeneration roadGeneration, bool includeTerminals = false)
    {
        List<Road> nonTerminalRoads = new List<Road>();
        
        foreach (int xLevel in system.Keys)
        {
            if (system[xLevel] is null) continue;

            foreach (int yLevel in system[xLevel].Keys)
            {
                Road _road = system[xLevel][yLevel];
                int[] _roadPosition = _road.GetPosition();

                // Check if has any free axes to connect with
                if (_road.GetData().axes.All(axis =>
                        !RoadExistsAt(_roadPosition[0] + roadGeneration.AxisToOffset(axis, 'x'), _roadPosition[1] + roadGeneration.AxisToOffset(axis, 'y')))) continue;
                
                // Check if is not terminal
                if (!roadGeneration.RoadIsTerminal(_road.GetData())) nonTerminalRoads.Add(_road);
                else if (includeTerminals) nonTerminalRoads.Add(_road);
            }
        }

        return nonTerminalRoads;
    }

    public void Reset()
    {
        system = new Dictionary<int, Dictionary<int, Road>>();
        roadsInSystem = 0;
    }
    
}

public struct RoadPacket
{
    public readonly int x, y;
    public readonly string axis;
    public readonly RoadData roadData;
    public bool requiresCorners;

    public RoadPacket(int _x, int _y, string _axis, RoadData _roadData, bool _requiresCorners = false)
    {
        x = _x;
        y = _y;
        axis = _axis;
        roadData = _roadData;
        requiresCorners = _requiresCorners;
    }

    public override string ToString()
    {
        return $"({x}, {y}) on {axis} => {roadData}";
    }
}
