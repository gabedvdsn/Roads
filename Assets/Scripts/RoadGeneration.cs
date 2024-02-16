using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Unity.VisualScripting;
using UnityEngine;
using Random = UnityEngine.Random;

public class RoadGeneration : MonoBehaviour
{
    
    [Header("Simulation Info")] 
    // If neither bool is true, use both square and diagonal roads
    [SerializeField] private bool useOnlySquareRoads;
    [SerializeField] private bool useOnlyDiagonalRoads;
    
    [Space]
    
    [Header("GenerationRule")]
    [SerializeField] private bool useRules;
    [SerializeField] private int separation;
    [SerializeField] [Tooltip("Tendency for roads to continue along the same generationAxis it was generated on")] private int linearityCoefficient;
    [SerializeField] [Range(0, 1)] private float curveCoefficient;
    [SerializeField] [Range(0, 1)] private float deltaCurveCoefficient;
    [SerializeField] private AnimationCurve linearityCurve;
    [Space] 
    [SerializedDictionary("Rule", "Priority")] 
    [SerializeField] private SerializedDictionary<GenerationRule, int> rulePriorities;

    private RuleMatrixInteger separationMatrix;
    private RuleMatrixAxialFloat linearityMatrix;
    
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
    
    [Space]

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
        separationMatrix = new RuleMatrixInteger();
        linearityMatrix = new RuleMatrixAxialFloat();
    }

    void ResetRuleMatrices()
    {
        separationMatrix.Reset();
        linearityMatrix.Reset();
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

        separationMatrix.Log();
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
        PassableRoadPacket startPacket = new PassableRoadPacket(0, 0, roadData.axes[0], roadData, null, diagonalRoadDatas.Contains(startRoad.GetData()));
        startRoad.Initialize(startPacket);
        
        // Check if we need to add corners
        if (startPacket.requiresCorners) InstantiateCornerRoads(startRoad, startPacket);
        
        // Add to roadSystem
        roadSystem.AddAt(startRoad, startPacket, 0, 0);

        // Generate off of startRoad manually
        List<PassableRoadPacket> validConnections = GetValidConnectionRoadsFor(startRoad);
        PassableRoadPacket nextPassableRoadPacket = GetSomeNonTerminalRoadPacket(validConnections);

        InstantiateRoad(startRoad, nextPassableRoadPacket);
    }

    void GenerateRoads()
    {
        foreach (Road parentRoad in roadSystem.GetAllFreeNonTerminalRoads(this))
        {
            if (roadSystem.GetSize() >= generationDepth * generationMagnitude) return;
            
            List<PassableRoadPacket> validConnections = GetValidConnectionRoadsFor(parentRoad);

            if (useRules) validConnections = ApplyRules(parentRoad, validConnections);
            
            if (validConnections.Count == 0) continue;  // exceedingly rare case when neighboring roads on all axes need to connect (just haven't made this art yet lol)

            InstantiateRoad(parentRoad, GetSomeNonTerminalRoadPacket(validConnections));
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
                
                // Check if terminalData is valid there
                if (RoadIsValidAtPosition(terminalData, offsetPosition[0], offsetPosition[1]))
                {
                    // The terminal is valid, let's create it
                    PassableRoadPacket packet = new PassableRoadPacket(offsetPosition[0], offsetPosition[1], axis, terminalData, _road, diagonalTerminalDatas.Contains(terminalData));
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
    
    void InstantiateRoad(Road parentRoad, PassableRoadPacket roadPacket)
    {
        // Where currRoad is the road to build off of and roadPacket contains the road to build
        
        Vector3 position = GetConnectionPosOnAxis(parentRoad, roadPacket.roadData.prefab, roadPacket.generationAxis);
        
        GameObject go = Instantiate(roadPacket.roadData.prefab, 
            position, 
            Quaternion.identity, 
            transform);
        
        go.GetComponent<Road>().Initialize(roadPacket);
        
        roadSystem.AddAt(go.GetComponent<Road>(), roadPacket, roadPacket.x, roadPacket.y);
        
        if (roadPacket.requiresCorners) InstantiateCornerRoads(parentRoad, roadPacket);
        
        // Impact rule matrices
        if (useRules) ImpactRuleMatrices(roadPacket);
    }

    void InstantiateCornerRoads(Road parentRoad, PassableRoadPacket roadPacket)
    {
        // if (!ShouldCreateCornersOnAxis(currRoad, roadPacket)) return;
        
        (string, string)[] axes = GetParallelCornerAxes(roadPacket.generationAxis);
        
        foreach ((string, string) axis in axes)
        {
            RoadData corner = FindCornerByAxis(axis.Item1);
            
            Instantiate(
                corner.prefab,
                GetConnectionPosOnAxis(parentRoad, parentRoad.GetData().prefab, axis.Item2),  // Use position relative to currRoad and currRoad to match its corner
                Quaternion.identity,
                parentRoad.transform);
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

    bool RoadIsValidAtPosition(RoadData potentialRoadData, int x, int y)
    {
        // Make sure potential road adheres to simulation rules
        if (potentialRoadData.axes.Any(axis => !validAxes.Contains(axis))) return false;
        
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
                !connectionsByAxis[potentialRoadData].ContainsKey(axis)) continue;
            
            // Check if existingRoad connects to potentialRoad along opposing generationAxis, if not, return false
            if (!(connectionsByAxis[existingRoad.GetData()].ContainsKey(GetOpposingAxis(axis)) && potentialRoadData.axes.Contains(axis))) return false;
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

    string[] GetOrthogobalAxes(string axis)
    {
        return axis switch
        {
            "up" => new []{"left", "right"},
            "down" => new []{"up", "down"},
            "left" => new []{"up", "down"},
            "right" => new []{"left", "right"},
            "upleft" => new []{"downleft", "upright"},
            "upright" => new []{"upleft", "downright"},
            "downleft" => new []{"upleft", "downright"},
            "downright" => new []{"downleft", "upright"},
            _ => Array.Empty<string>()
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

    public bool RoadIsTerminal(RoadData roadData) => validTerminals.Contains(roadData);
    
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

    Vector3 GetConnectionPosOnAxis(Road parentRoad, GameObject otherPrefab, string axis)
    {
        // I promise these parameters make sense!
        // This method uses the rect sizes & ppu values for the sprites contained in currRoad & nextPrefab to determine the required offset position on generationAxis
        
        Vector3 currPosition = parentRoad.transform.position;
        
        Sprite thisSprite = parentRoad.GetData().prefab.GetComponent<SpriteRenderer>().sprite;
        Sprite nextSprite = otherPrefab.GetComponent<SpriteRenderer>().sprite;

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

    Vector3 GetLeftConnectionPos(Vector3 parentPosition, Sprite parentSprite, Sprite otherSprite)
    {
        return parentPosition + new Vector3(
            -GetRelativeSpriteSizeX(parentSprite, otherSprite),
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

    List<PassableRoadPacket> GetValidConnectionRoadsFor(Road parentRoad)
    {
        // Returns a list of RoadPackets where each PassableRoadPacket represents the position, generationAxis,
        // and data of new road to create off of _road
        
        RoadData roadData = parentRoad.GetData();
        int[] roadPosition = parentRoad.GetPosition();

        List<PassableRoadPacket> validConnections = new List<PassableRoadPacket>();

        foreach (string axis in connectionsByAxis[roadData].Keys)
        {
            // Make sure generationAxis is valid
            foreach (RoadData potentialRoadData in connectionsByAxis[roadData][axis])
            {
                int _x = roadPosition[0] + AxisToOffset(axis, 'x');
                int _y = roadPosition[1] + AxisToOffset(axis, 'y');
                
                if (roadSystem.RoadExistsAt(_x, _y)) continue;

                if (!RoadIsValidAtPosition(potentialRoadData, _x, _y)) continue;

                bool requiresCorners = diagonalRoadDatas.Contains(potentialRoadData);  //&& !diagonalRoadDatas.Contains(_road.GetData());
                validConnections.Add(new PassableRoadPacket(_x, _y, axis, potentialRoadData, parentRoad, requiresCorners));
            }
        }

        // Where validConnections is a list of valid PassableRoadPackets on ALL axes of _road, not just a single axis
        return validConnections;

    }

    List<PassableRoadPacket> ApplyRules(Road parentRoad, List<PassableRoadPacket> validConnections, List<PassableRoadPacket> _modifiedValidConnections = null, GenerationRule[] _skipRules = null, int _skipIteration = 0, bool _alwaysApplyRules = true)
    {
        /*if (_modifiedValidConnections != null && _modifiedValidConnections.Count ! > 0)
        {
            return _skipRules is null ? _modifiedValidConnections : ApplyRules(parentRoad, validConnections, _skipRules: _skipRules.Skip(_skipIteration).Take(_skipRules.Length - _skipIteration).ToArray(), _skipIteration: _skipIteration + 1);
        }*/
        
        List<PassableRoadPacket> modifiedValidConnections = new List<PassableRoadPacket>();
        
        // Apply rules by priority
        foreach (GenerationRule rule in rulePriorities.Keys.ToArray().OrderBy(rule => rulePriorities[rule]))
        {
            if (_skipRules is not null)
            {
                if (_skipRules.Contains(rule)) continue;
            }
            
            modifiedValidConnections = ApplyRule(rule, parentRoad, validConnections);
        }
        
        return modifiedValidConnections;
    }

    List<PassableRoadPacket> ApplyRule(GenerationRule rule, Road parentRoad, List<PassableRoadPacket> validConnections)
    {
        return rule switch
        {
            GenerationRule.Separation => ApplySeparationRule(validConnections),  // separationRule does not need to see the parent road
            GenerationRule.Linearity => ApplyLinearityRule(parentRoad, validConnections),
            _ => validConnections
        };
    }
    
    List<PassableRoadPacket> ApplySeparationRule(List<PassableRoadPacket> validConnections)
    {
        List<PassableRoadPacket> modifiedValidConnections = new List<PassableRoadPacket>();
        
        foreach (PassableRoadPacket packet in validConnections)
        {
            if (separationMatrix.GetAt(packet.x, packet.y) < separation) continue;
            modifiedValidConnections.Add(packet);
        }

        return modifiedValidConnections;
    }

    List<PassableRoadPacket> ApplyLinearityRule(Road parentRoad, List<PassableRoadPacket> validConnections)
    {
        int[] position = parentRoad.GetPosition();
        bool useLinear = Random.Range(0f, 1f) <
                         linearityMatrix.GetAtOnAxis(position[0], position[1], parentRoad.GetGenerationAxis());

        return validConnections.Where(packet => !useLinear || packet.generationAxis == parentRoad.GetGenerationAxis()).ToList();
    }

    void ImpactRuleMatrices(PassableRoadPacket packet)
    {
        foreach (GenerationRule rule in rulePriorities.Keys.ToArray().OrderBy(rule => rulePriorities[rule]))
            ImpactRuleMatrix(rule, packet);

    }

    void ImpactRuleMatrix(GenerationRule rule, PassableRoadPacket packet)
    {
        Debug.Log(rule);
        switch(rule)
        {
            case GenerationRule.Separation:
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
        // Starts at 0, goes to separation - 1
        Debug.Log($"Impacting separation => {packet}");
        
        separationMatrix.SetAt(packet.x, packet.y, -1);
        
        foreach (string axis in GetOrthogobalAxes(packet.generationAxis))
        {
            for (int i = 1; i <= separation; i++)
            {
                separationMatrix.SetAt(packet.x + (i * AxisToOffset(axis, 'x')), packet.x + (i * AxisToOffset(axis, 'y')), i - 1);
            }
        }
    }
    
    void ImpactLinearityMatrix(PassableRoadPacket packet)
    {
        // Set linearity value along generation axis
        float linearityOnAxis = Mathf.Clamp01(linearityCurve.Evaluate(linearityMatrix.GetAtOnAxis(packet.x, packet.y, packet.generationAxis)) - deltaCurveCoefficient);
        int x = packet.x + AxisToOffset(packet.generationAxis, 'x');
        int y = packet.y + AxisToOffset(packet.generationAxis, 'y');
        
        linearityMatrix.SetAtOnAxis(x, y, packet.generationAxis, linearityOnAxis);
        
        // Set value in matrix to -1 to indicate that a road is already placed there
        linearityMatrix.SetAtOnAxis(packet.x, packet.y, packet.generationAxis, -1f);
    }
    
    #endregion
    
    #region Editor
    
    public void OnClickReset()
    {
        DestroyAllRoadsInSystem();

        ResetRuleMatrices();
        
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

public class RuleMatrixInteger
{
    private Dictionary<int, Dictionary<int, int>> matrix;

    public RuleMatrixInteger()
    {
        matrix = new Dictionary<int, Dictionary<int, int>>();
    }

    public void SetAt(int x, int y, int value)
    {
        Debug.Log($"Set {value} at {x}, {y}");
        if (matrix.ContainsKey(x))
        {
            if (matrix[x] is null)
            {
                matrix[x] = new Dictionary<int, int>() { { y, value } }; // y level not initialized, initialize y level
            }
            else if (!matrix[x].ContainsKey(y))
            {
                matrix[x][y] = value; // y level initialized, but [x][y] does not exist, add
            }
            else
            {
                matrix[x][y] = value;
            }
        }
        else
        {
            matrix[x] = new Dictionary<int, int>()
                { { y, value } }; // x level does not exist, initialize y level and add
        }
    }

    public void AddAt(int x, int y, int value)
    {
        if (matrix.ContainsKey(x))
        {
            if (matrix[x] is null)
            {
                matrix[x] = new Dictionary<int, int>() { { y, value } }; // y level not initialized, initialize y level
            }
            else if (!matrix[x].ContainsKey(y))
            {
                matrix[x][y] = value; // y level initialized, but [x][y] does not exist, add
            }
            else
            {
                matrix[x][y] += value;
            }
        }
        else
        {
            matrix[x] = new Dictionary<int, int>()
                { { y, value } }; // x level does not exist, initialize y level and add
        }
    }

    public int GetAt(int x, int y) => ValueExistsAt(x, y) ? matrix[x][y] : int.MaxValue;

    private bool ValueExistsAt(int x, int y)
    {
        if (matrix.ContainsKey(x)) return matrix[x] is not null && matrix[x].ContainsKey(y);

        return false;
    }

    public void Log()
    {
        string output = "";

        foreach (int x in matrix.Keys)
        {
            foreach (int y in matrix[x].Keys)
            {
                output += $"({x}, {y}) => {GetAt(x, y)}";
            }

            output += "\n";
        }

        Debug.Log(output);
    }
    
    public void Reset() => matrix.Clear();
}

public class RuleMatrixAxialFloat
{
    private Dictionary<int, Dictionary<int, Dictionary<string, float>>> matrix;

    public RuleMatrixAxialFloat()
    {
        matrix = new Dictionary<int, Dictionary<int, Dictionary<string, float>>>();
    }

    public void SetAtOnAxis(int x, int y, string axis, float value)
    {
        if (matrix[x] is null)
        {
            Dictionary<string, float> axisDict = new Dictionary<string, float>() { { axis, value } };
            matrix[x] = new Dictionary<int, Dictionary<string, float>>() { { y, axisDict } }; // y level not initialized, initialize y level
        }
        else if (!matrix[x].ContainsKey(y))
        {
            matrix[x][y] = new Dictionary<string, float>() { { axis, value } }; // y level initialized, but [x][y] does not exist, add
        }
        else
        {
            matrix[x][y][axis] = value;
        }
    }

    public void AddAtOnAxis(int x, int y, string axis, float value)
    {
        if (matrix.ContainsKey(x))
        {
            if (matrix[x] is null)
            {
                Dictionary<string, float> axisDict = new Dictionary<string, float>() { { axis, value } };
                matrix[x] = new Dictionary<int, Dictionary<string, float>>() { { y, axisDict } }; // y level not initialized, initialize y level
            }
            else if (!matrix[x].ContainsKey(y))
            {
                matrix[x][y] = new Dictionary<string, float>() { { axis, value } }; // y level initialized, but [x][y] does not exist, add
            }
            else if (!matrix[x][y].ContainsKey(axis))
            {
                matrix[x][y][axis] = value;
            }
            else
            {
                matrix[x][y][axis] += value;
            }
        }
        else
        {
            Dictionary<string, float> axisDict = new Dictionary<string, float>() { { axis, value } };
            matrix[x] = new Dictionary<int, Dictionary<string, float>>() { { y, axisDict } };
        }
    }

    public float GetAtOnAxis(int x, int y, string axis) => ValueAndAxisExistAt(x, y, axis) ? matrix[x][y][axis] : -1f;
    
    private bool ValueAndAxisExistAt(int x, int y, string axis)
    {
        if (!matrix.ContainsKey(x)) return false;
        
        return matrix[x].ContainsKey(y) && matrix[x][y].ContainsKey(axis);

    }

    public void Reset() => matrix.Clear();
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

    public PassableRoadPacket(int _x, int _y, string _generationAxis, RoadData _roadData, Road parentRoad, bool _requiresCorners = false)
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

    public static PassableRoadPacket Empty() => new PassableRoadPacket(0, 0, "", null, null);
}

public enum GenerationRule
{
    Separation,
    Linearity
}