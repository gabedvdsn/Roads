using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class RoadGeneration : MonoBehaviour
{
    
    [Header("Simulation Info")] 
    // If neither bool is true, use both square and diagonal roads
    [SerializeField] private bool useOnlySquareRoads;
    [SerializeField] private bool useOnlyDiagonalRoads;
    
    [Space]
    
    [Header("GenerationRule")]
    [SerializeField] private int separation = 0;
    [SerializeField] [Tooltip("Tendency for roads to continue along the same generationAxis it was generated on")] private int linearityCoefficient;
    [SerializeField] private AnimationCurve linearityCurve;
    [Space] 
    [SerializedDictionary("Rule", "Priority")] 
    [SerializeField] private SerializedDictionary<GenerationRule, int> rulePriorities;

    private RuleMatrix separationMatrix;
    private RuleMatrix linearityMatrix;
    
    [Space]
    
    [Header("Road Info")]
    [SerializeField] private RoadData[] squareRoadDatas;
    [SerializeField] private RoadData[] squareTerminalDatas;
    [Space]
    [SerializeField] private RoadData[] diagonalRoadDatas;
    [SerializeField] private RoadData[] diagonalCornerRoadDatas;
    [SerializeField] private RoadData[] diagonalTerminalDatas;


    private RoadData[] allRoadDatas;
    private string[] validAxes;
    private RoadData[] validTerminals;
    
    private int generationDepth;
    private float generationMagnitude;

    [Header("UI Info")] 
    [SerializeField] private TMPro.TMP_InputField generationDepthField;
    [SerializeField] private TMPro.TMP_InputField generationMagnitudeField;

    private RoadSystem roadSystem;
    private Dictionary<RoadData, Dictionary<string, List<RoadData>>> connectionsByAxis;

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
        
        Setup();
    }

    #region Setup

    void Setup()
    {
        CreateVariablesBySettings();
        CreateRuleMatrices();
        CreateConnectionsByAxis();
    }
    
    void CreateVariablesBySettings()
    {
        if (!useOnlySquareRoads && !useOnlyDiagonalRoads)
        {
            allRoadDatas = squareRoadDatas.Concat(diagonalRoadDatas).ToArray();
            validAxes = squareAxes.Concat(diagonalAxes).ToArray();
            validTerminals = squareTerminalDatas.Concat(diagonalTerminalDatas).ToArray();
        }
        else if (useOnlySquareRoads && !useOnlyDiagonalRoads)
        {
            allRoadDatas = squareRoadDatas;
            validAxes = squareAxes;
            validTerminals = squareTerminalDatas;
        }
        else if (!useOnlySquareRoads && useOnlyDiagonalRoads)
        {
            allRoadDatas = diagonalRoadDatas;
            validAxes = diagonalAxes;
            validTerminals = squareTerminalDatas;
        }
        
        // In the case that both flags are true, allRoadDatas remains empty
    }

    void CreateRuleMatrices()
    {
        separationMatrix = new RuleMatrix();
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
        
        Debug.Log($"Generation Depth => {generationDepth * generationMagnitude}");
        
        GenerateStart();
        
        // Do generation
        for (int _ = 0; _ < generationDepth; ++_) GenerateRoads();
        
        // Close all remaining non-terminal roads with terminal roads
        TerminateAllRoads();
        
        Debug.Log($"Roads in system => {roadSystem.GetSize()}");
    }
    
    void GenerateStart(int startX = 0, int startY = 0)
    {
        RoadData roadData = GetTerminalForAxis(validAxes[Random.Range(0, validAxes.Length)]);

        if (roadData is null)
        {
            Debug.Log($"Could not generate start because initial RoadData is null");
            return;
        }
        
        // Instantiate start road
        Vector3 position = new Vector3(startX, startY, 0);
        GameObject go = Instantiate(roadData.prefab, position, Quaternion.identity, transform);
        
        // Initialize start road
        Road startRoad = go.GetComponent<Road>();
        startRoad.Initialize(startX, startY);
        
        // Check if we need to add corners
        if (diagonalRoadDatas.Contains(startRoad.GetData()))
        {
            PassableRoadPacket startPassableRoadPacket = new PassableRoadPacket(0, 0, startRoad.GetData().axes[0], startRoad.GetData(), true);
            InstantiateCornerRoads(startRoad, startPassableRoadPacket);
        }
        
        roadSystem.AddAt(go.GetComponent<Road>(), new PassableRoadPacket(0, 0, startRoad.GetData().axes[0], startRoad.GetData()), 0, 0);

        // Generate off of startRoad manually
        List<PassableRoadPacket> validConnections = GetValidConnectionRoadsFor(go.GetComponent<Road>());
        PassableRoadPacket nextPassableRoadPacket = GetSomeNonTerminalRoadPacket(validConnections);

        InstantiateRoad(startRoad, nextPassableRoadPacket);

    }

    void GenerateRoads()
    {
        foreach (Road _road in roadSystem.GetAllFreeNonTerminalRoads(this))
        {
            if (roadSystem.GetSize() >= generationDepth * generationMagnitude) return;
            
            List<PassableRoadPacket> validConnections = GetValidConnectionRoadsFor(_road);

            validConnections = ApplyRules(_road, validConnections);
            
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
                    RoadData terminal = GetTerminalForAxis(axis);
                    PassableRoadPacket packet = new PassableRoadPacket(offsetPosition[0], offsetPosition[1], axis, terminal, diagonalTerminalDatas.Contains(terminal));
                    InstantiateRoad(_road, packet);
                }
                else
                {
                    // The terminal is invalid, create any valid road instead
                    List<PassableRoadPacket> validConnectionRoads = GetValidConnectionRoadsFor(_road);

                    if (validConnectionRoads.Count == 0) continue;  // exceedingly rare case when neighboring roads on all axes need to connect
                    
                    InstantiateRoad(_road, validConnectionRoads[Random.Range(0, validConnectionRoads.Count)]);
                    
                    // Non-terminal is instantiated
                    reTerminate = true;
                }
            }
        }
        
        // If a non-terminal has been instantiated, re-run method to account for possible new unconnected axes
        // Recursion is guaranteed to terminate because any non-terminal must either connect to a neighboring road or terminate on the proceeding cycle
        if (reTerminate) TerminateAllRoads();
    }
    
    void InstantiateRoad(Road currRoad, PassableRoadPacket roadPacket)
    {
        // Where currRoad is the road to build off of and roadPacket contains the road to build
        
        Vector3 position = GetConnectionPosOnAxis(currRoad, roadPacket.roadData.prefab, roadPacket.generationAxis);
        
        GameObject go = Instantiate(roadPacket.roadData.prefab, 
            position, 
            Quaternion.identity, 
            transform);
        
        go.GetComponent<Road>().Initialize(roadPacket.x, roadPacket.y);
        
        roadSystem.AddAt(go.GetComponent<Road>(), roadPacket, roadPacket.x, roadPacket.y);
        
        if (roadPacket.requiresCorners) InstantiateCornerRoads(currRoad, roadPacket);
        
        // Can we delete any overlapping corners?
        
        // If currRoad is diagonal along roadPacket.generationAxis, we can assume it has already created corners
        // Therefore, we can safely Destroy() the corners created for the most recent road
        
    }

    void InstantiateCornerRoads(Road currRoad, PassableRoadPacket roadPacket)
    {
        // if (!ShouldCreateCornersOnAxis(currRoad, roadPacket)) return;
        
        (string, string)[] axes = GetParallelCornerAxes(roadPacket.generationAxis);
        
        foreach ((string, string) axis in axes)
        {
            RoadData corner = FindCornerByAxis(axis.Item1);
            
            Instantiate(
                corner.prefab,
                GetConnectionPosOnAxis(currRoad, currRoad.GetData().prefab, axis.Item2),  // Use position relative to currRoad and currRoad to match its corner
                Quaternion.identity,
                currRoad.transform);
        }
    }
    
    void DestroyAllRoadsInSystem()
    {
        // foreach (Road _road in roadSystem.GetAllFreeNonTerminalRoads(this, includeTerminals: true)) Destroy(_road);
        for (int i = 0; i < transform.childCount; ++i) Destroy(transform.GetChild(i).gameObject);
    }

    #endregion
    
    #region Calculation
    
    RoadData FindRoadByName(string _name, IEnumerable<RoadData> collection) => collection.FirstOrDefault(data => data.roadName == _name);

    RoadData FindCornerByAxis(string axis)
    {
        return axis switch
        {
            "upleft" => FindRoadByName("CornerUpLeft", diagonalCornerRoadDatas),
            "upright" => FindRoadByName("CornerUpRight", diagonalCornerRoadDatas),
            "downleft" => FindRoadByName("CornerDownLeft", diagonalCornerRoadDatas),
            "downright" => FindRoadByName("CornerDownRight", diagonalCornerRoadDatas),
            _ => null
        };
    }

    bool PotentialRoadIsValidAtPosition(RoadData potentialRoad, int x, int y)
    {
        // Make sure potential road adheres to simulation rules
        if (potentialRoad.axes.Any(axis => !validAxes.Contains(axis))) return false;
        
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
            
            // Check if existingRoad connects to potentialRoad along opposing generationAxis, if not, return false
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

    (string, string)[] GetParallelCornerAxes(string axis)
    {
        // Returns an array of tuples in the form { ( corner generationAxis, generationAxis ), ( corner generationAxis, generationAxis) }
        // where corner
        
        return axis switch
        {
            "upleft" => new [] { ("downleft", "up"), ("upright", "left") },
            "upright" => new [] { ("downright", "up"), ("upleft", "right") },
            "downleft" => new [] { ("upleft", "down"), ("downright", "left") },
            "downright" => new [] { ("upright", "down"), ("downleft", "right") },
            _ => Array.Empty<(string, string)>()
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
                "upright" => 1,
                "downleft" => -1,
                "downright" => -1,
                _ => 0
            },
            _ => 0
        };
    }

    public bool RoadIsTerminal(RoadData _roadData) => validTerminals.Contains(_roadData);
    
    PassableRoadPacket GetSomeNonTerminalRoadPacket(List<PassableRoadPacket> validConnections)
    {
        PassableRoadPacket packet = validConnections[Random.Range(0, validConnections.Count)];
        
        int attempts = 0;
                
        while (RoadIsTerminal(packet.roadData))
        {
            if (attempts >= validTerminals.Length) break;
                    
            packet = validConnections[Random.Range(0, validConnections.Count)];
            attempts += 1;
        }
        
        return packet;
    }

    RoadData GetTerminalForAxis(string axis) => FindRoadByName(GetOpposingAxis(axis).FirstCharacterToUpper(), allRoadDatas);

    Vector3 GetConnectionPosOnAxis(Road currRoad, GameObject nextPrefab, string axis)
    {
        // I promise these parameters make sense!
        // This method uses the rect sizes & ppu values for the sprites contained in currRoad & nextPrefab to determine the required offset position on generationAxis
        
        Vector3 currPosition = currRoad.transform.position;
        
        Sprite thisSprite = currRoad.GetData().prefab.GetComponent<SpriteRenderer>().sprite;
        Sprite nextSprite = nextPrefab.GetComponent<SpriteRenderer>().sprite;

        return axis switch
        {
            "left" => GetLeftConnectionPos(currPosition, thisSprite, nextSprite),
            "right" => GetRightConnectionPos(currPosition, thisSprite, nextSprite),
            "up" => GetUpConnectionPos(currPosition, thisSprite, nextSprite),
            "down" => GetDownConnectionPos(currPosition, thisSprite, nextSprite),
            "upleft" => GetUpLeftConnectionPos(currPosition, thisSprite, nextSprite),
            "upright" => GetUpRightConnectionPos(currPosition, thisSprite, nextSprite),
            "downleft" => GetDownLeftConnectionPos(currPosition, thisSprite, nextSprite),
            "downright" => GetDownRightConnectionPos(currPosition, thisSprite, nextSprite),
            _ => Vector3.zero
        };
    }

    Vector3 GetLeftConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            -GetRelativeSpriteSizeX(thisSprite, otherSprite),
            transform.position.y,
            0);
    }
    
    Vector3 GetRightConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            GetRelativeSpriteSizeX(thisSprite, otherSprite),
            transform.position.y,
            0);
    }
    
    Vector3 GetUpConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            transform.position.x,
            GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);
    }
    
    Vector3 GetDownConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            transform.position.x,
            -GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);
    }

    Vector3 GetUpLeftConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            -GetRelativeSpriteSizeX(thisSprite, otherSprite),
            GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);
    }

    Vector3 GetUpRightConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            GetRelativeSpriteSizeX(thisSprite, otherSprite),
            GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);    
    }
    
    Vector3 GetDownLeftConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            -GetRelativeSpriteSizeX(thisSprite, otherSprite),
            -GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);    
    }
    
    Vector3 GetDownRightConnectionPos(Vector3 thisPosition, Sprite thisSprite, Sprite otherSprite)
    {
        return thisPosition + new Vector3(
            GetRelativeSpriteSizeX(thisSprite, otherSprite),
            -GetRelativeSpriteSizeY(thisSprite, otherSprite),
            0);    
    }

    float GetRelativeSpriteSizeX(Sprite thisSprite, Sprite otherSprite)
    {
        return thisSprite.textureRect.width / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.width / otherSprite.pixelsPerUnit / 2;
    }

    float GetRelativeSpriteSizeY(Sprite thisSprite, Sprite otherSprite)
    {
        return thisSprite.textureRect.height / thisSprite.pixelsPerUnit / 2 + otherSprite.textureRect.height / otherSprite.pixelsPerUnit / 2;
    }
    
    #endregion
    
    #region Generation Rules

    List<PassableRoadPacket> GetValidConnectionRoadsFor(Road _road)
    {
        // Returns a list of RoadPackets where each PassableRoadPacket represents the position, generationAxis,
        // and data of new road to create off of _road
        
        RoadData roadData = _road.GetData();
        int[] roadPosition = _road.GetPosition();

        List<PassableRoadPacket> validConnectionRoads = new List<PassableRoadPacket>();

        foreach (string axis in connectionsByAxis[roadData].Keys)
        {
            // Make sure generationAxis is valid
            foreach (RoadData potentialRoadData in connectionsByAxis[roadData][axis])
            {
                int _x = roadPosition[0] + AxisToOffset(axis, 'x');
                int _y = roadPosition[1] + AxisToOffset(axis, 'y');
                
                if (roadSystem.RoadExistsAt(_x, _y)) continue;

                if (!PotentialRoadIsValidAtPosition(potentialRoadData, _x, _y)) continue;

                bool requiresCorners = diagonalRoadDatas.Contains(potentialRoadData);  //&& !diagonalRoadDatas.Contains(_road.GetData());
                validConnectionRoads.Add(new PassableRoadPacket(_x, _y, axis, potentialRoadData, requiresCorners));
            }
        }

        return validConnectionRoads;

    }

    List<PassableRoadPacket> ApplyRules(Road _road, List<PassableRoadPacket> validConnections)
    {
        if (validConnections.Count !> 0) return validConnections;
        
        List<PassableRoadPacket> modifiedValidConnections = new List<PassableRoadPacket>();
        
        // Apply rules by priority
        foreach (GenerationRule rule in rulePriorities.Keys.ToArray().OrderBy(rule => rulePriorities[rule]).Reverse()) modifiedValidConnections = ApplyRule(rule, _road, validConnections);

        return modifiedValidConnections;
    }

    List<PassableRoadPacket> ApplyRule(GenerationRule rule, Road _road, List<PassableRoadPacket> validConnections)
    {
        return rule switch
        {
            GenerationRule.Separation => ApplySeparationRule(_road, validConnections),
            GenerationRule.Linearity => ApplyLinearityRule(_road, validConnections),
            _ => validConnections
        };
    }
    
    List<PassableRoadPacket> ApplySeparationRule(Road _road, List<PassableRoadPacket> validConnections)
    {
        List<PassableRoadPacket> modifiedValidConnections = new List<PassableRoadPacket>();
        
        PassableRoadPacket bestPacket = validConnections[0];
        int bestSeparation = GetMinRoadSeparation(bestPacket);
        
        foreach (PassableRoadPacket packet in validConnections)
        {
            
        }

        return modifiedValidConnections;
    }

    List<PassableRoadPacket> ApplyLinearityRule(Road _road, List<PassableRoadPacket> validConnections)
    {
        List<PassableRoadPacket> modifiedValidConnections = new List<PassableRoadPacket>();



        return modifiedValidConnections;
    }

    void ImpactRuleMatrices(PassableRoadPacket packet)
    {
        foreach (GenerationRule rule in (GenerationRule[])Enum.GetValues(typeof(GenerationRule))) ImpactRuleMatrix(rule, packet);
    }

    void ImpactRuleMatrix(GenerationRule rule, PassableRoadPacket packet)
    {
        switch(rule)
        {
            case GenerationRule.Separation:
                if (separation ! > 0) break;
                
                ImpactSeparationMatrix(packet);
                break;
            
            case GenerationRule.Linearity:
                if (linearityCoefficient ! > 0) break;
                
                ImpactLinearityMatrix(packet);
                break;
            
            default:
                return;
        };
    }
    
    void ImpactSeparationMatrix(PassableRoadPacket packet)
    {
        
    }
    
    void ImpactLinearityMatrix(PassableRoadPacket packet)
    {
        
    }

    int GetMinRoadSeparation(PassableRoadPacket packet)
    {
        foreach (string axis in validAxes)
        {
            int sepCounter = 0;
            int x = packet.x;
            int y = packet.y;

            while (sepCounter < separation)
            {
                

                sepCounter += 1;
            }
        }

        return 0;
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

        if (Application.isPlaying) Setup();
    }
    
    #endregion
}

public class RoadSystem
{
    private Dictionary<int, Dictionary<int, StorableRoadPacket>> system = new();
    private int roadsInSystem;

    public int GetSize() => roadsInSystem;

    public void AddAt(Road road, PassableRoadPacket packet, int x, int y)
    {
        if (system.ContainsKey(x))
        {
            if (system[x] is null)
            {
                system[x] = new Dictionary<int, StorableRoadPacket>() { { y, new StorableRoadPacket(road, packet) } }; // y level not initialized, initialize y level
                roadsInSystem += 1;
            }
            else if (!system[x].ContainsKey(y))
            {
                system[x][y] = new StorableRoadPacket(road, packet); // y level initialized, but [x][y] does not exist, add
                roadsInSystem += 1;
            }
        }
        else
        {
            system[x] = new Dictionary<int, StorableRoadPacket>() { { y, new StorableRoadPacket(road, packet) } };  // x level does not exist, initialize y level and add
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
        _road = RoadExistsAt(x, y) ? system[x][y].road : null;

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
                Road _road = system[xLevel][yLevel].road;
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
        system.Clear();
        roadsInSystem = 0;
    }
    
}

public class RuleMatrix
{
    private readonly Dictionary<int, Dictionary<int, RuleMatrixPacket>> matrix;

    public RuleMatrix()
    {
        matrix = new Dictionary<int, Dictionary<int, RuleMatrixPacket>>();
    }

    public void Add(int x, int y, int value, float coefficient = 1f)
    {
        if (matrix.ContainsKey(x))
        {
            if (matrix[x] is null)
            {
                matrix[x] = new Dictionary<int, RuleMatrixPacket>() { { y, new RuleMatrixPacket(value, coefficient) } }; // y level not initialized, initialize y level
            }
            else if (!matrix[x].ContainsKey(y))
            {
                matrix[x][y] = new RuleMatrixPacket(value, coefficient); // y level initialized, but [x][y] does not exist, add
            }
        }
        else
        {
            matrix[x] = new Dictionary<int, RuleMatrixPacket>() { { y, new RuleMatrixPacket(value, coefficient) } };  // x level does not exist, initialize y level and add
        }
    }

    public int Get(int x, int y) => ValueExistsAt(x, y) ? matrix[x][y].Get() : 0;

    public float GetCoef(int x, int y) => ValueExistsAt(x, y) ? matrix[x][y].GetCoef() : 0f;

    public bool ValueExistsAt(int x, int y)
    {
        if (matrix.ContainsKey(x)) return matrix[x] is not null && matrix[x].ContainsKey(y);
        
        return false;
    }
}

public struct RuleMatrixPacket
{
    private int value;
    private float coefficient;

    public RuleMatrixPacket(int _value, float _coefficient)
    {
        value = _value;
        coefficient = _coefficient;
    }

    public int Get() => value;

    public float GetCoef() => value * coefficient;
}

public readonly struct StorableRoadPacket
{
    public readonly Road road;
    public readonly PassableRoadPacket packet;

    public StorableRoadPacket(Road _road, PassableRoadPacket _packet)
    {
        road = _road;
        packet = _packet;
    }
}

public readonly struct PassableRoadPacket
{
    // The PassableRoadPacket contains the information concerning the generation of a road
    public readonly int x, y;
    public readonly string generationAxis;
    public readonly RoadData roadData;
    public readonly bool requiresCorners;

    public PassableRoadPacket(int _x, int _y, string _generationAxis, RoadData _roadData, bool _requiresCorners = false)
    {
        x = _x;
        y = _y;
        generationAxis = _generationAxis;
        roadData = _roadData;
        requiresCorners = _requiresCorners;
    }

    public override string ToString()
    {
        return $"({x}, {y}) on {generationAxis} => {roadData}";
    }

    public static PassableRoadPacket Empty() => new PassableRoadPacket(0, 0, "", null);
}

public enum GenerationRule
{
    Separation,
    Linearity
}