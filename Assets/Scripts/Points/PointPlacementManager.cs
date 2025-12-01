using System;
using System.Collections.Generic;
using UnityEngine;

namespace Points
{
	/// <summary>
	/// Manages point placement state, data persistence, and events for point handles.
	/// </summary>
	public class PointPlacementManager : MonoBehaviour
	{
		[Serializable]
		public struct PointData
		{
			public int Id;
			public Vector3 Position;
			public Color Color;
			public float Radius;
			public DateTime CreatedAt;
			
			// Thesis Feature: Waypoint type system
			public WaypointType Type;
			public float YawDegrees;
			public Dictionary<string, object> Parameters;
			
			// ArduPilot/PX4 Integration: Autopilot-ready fields for indoor flight
			public float AcceptanceRadius;  // How close drone must get (meters) - default 0.25m for indoor
			public float HoldTime;          // How long to wait at waypoint (seconds)
			public float SpeedMS;           // Target speed in m/s (-1 = use default)
		}

		public event Action<PointData> OnPointPlaced;
		public event Action<PointHandle> OnPointSelected;
		public event Action<PointHandle, bool> OnPointHovered;

		[SerializeField] private float _minDepth = 0.2f;
		[SerializeField] private float _maxDepth = 10f;
		[SerializeField] private float _depthSpeed = 1.0f; // meters per second per stick unit
		[SerializeField] private float _depthStep = 0.1f;
		[SerializeField] private float _precisionMultiplier = 0.1f;
		[SerializeField] private bool _surfaceSnappingEnabled = true;
		[SerializeField] private float _placedPointRadius = 0.05f;
		[SerializeField] private Color _placedPointColor = Color.yellow;
		[SerializeField] private Color _ghostValidColor = Color.green;
		[SerializeField] private Color _ghostInvalidColor = Color.red;

	[SerializeField] private Transform _rightHandRayOrigin;
	[SerializeField] private Transform _ghostTransform;
	[SerializeField] private Renderer _ghostRenderer;
	[SerializeField] private PointLabelBillboard _depthReadout;
	[SerializeField] private HapticsHelper _hapticsHelper;
	[SerializeField] private RayDepthController _rayDepthController;
	[SerializeField] private PointHandle _pointHandlePrefab;
	[SerializeField] private Transform _pointsParent;
	
	// Thesis Feature: Collision avoidance parameters
	[SerializeField] private float _droneRadius = 0.45f; // 45 cm safety buffer (0.8m drone diameter / 2 + safety margin)
	[SerializeField] private LayerMask _environmentLayer = 1 << 0; // Default layer initially
	[SerializeField] private Color _collisionGhostColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // Grey semi-transparent

	// Record360 Feature: Two-step placement system
	[SerializeField] private RecordingHeightController _recordingHeightController;
	[SerializeField] private Transform _recordingGhostTransform; // Separate ghost for recording point
	[SerializeField] private Renderer _recordingGhostRenderer;
	[SerializeField] private Transform _anchorGhostTransform; // Visual anchor during height adjustment
	[SerializeField] private Renderer _anchorGhostRenderer;

	private readonly List<PointData> _points = new List<PointData>();
	private readonly Dictionary<int, PointHandle> _idToHandle = new Dictionary<int, PointHandle>();
	private int _nextId = 1;
	
	// Thesis Feature: Current waypoint type selection
	private WaypointType _currentTypeSelection = WaypointType.StopTurnGo;
	
	// Thesis Feature: Track highlighted obstacles for collision feedback
	private readonly HashSet<ObstacleHighlighter> _currentlyHighlightedObstacles = new HashSet<ObstacleHighlighter>();

	// Record360 Feature: Two-step placement state
	private enum RecordPlacementState
	{
		None,              // Not placing a record waypoint
		PlacingAnchor,     // Placing the anchor point (step 1)
		AdjustingHeight    // Adjusting the recording height (step 2)
	}

	private RecordPlacementState _recordPlacementState = RecordPlacementState.None;
	private Vector3 _pendingAnchorPosition;
	private float _pendingAnchorYaw;

		/// <summary>
		/// Minimum allowed placement depth in meters.
		/// </summary>
		public float MinDepth => _minDepth;

		/// <summary>
		/// Maximum allowed placement depth in meters.
		/// </summary>
		public float MaxDepth => _maxDepth;

		/// <summary>
		/// Continuous depth adjustment speed (m/s per stick unit).
		/// </summary>
		public float DepthSpeed => _depthSpeed;

		/// <summary>
		/// Step amount in meters for A/B adjustments.
		/// </summary>
		public float DepthStep => _depthStep;

		/// <summary>
		/// Multiplier applied while precision is held.
		/// </summary>
		public float PrecisionMultiplier => _precisionMultiplier;

		/// <summary>
		/// Whether surface snapping is enabled.
		/// </summary>
		public bool SurfaceSnappingEnabled => _surfaceSnappingEnabled;

		/// <summary>
		/// Color used for valid ghost preview.
		/// </summary>
		public Color GhostValidColor => _ghostValidColor;

		/// <summary>
		/// Color used for invalid/clamped ghost preview.
		/// </summary>
		public Color GhostInvalidColor => _ghostInvalidColor;

		/// <summary>
		/// Default radius used for spawned point handles.
		/// </summary>
		public float PlacedPointRadius => _placedPointRadius;

		/// <summary>
		/// Default color used for spawned point handles.
		/// </summary>
		public Color PlacedPointColor => _placedPointColor;

		/// <summary>
		/// Prefab used for creating placed points.
		/// </summary>
		public PointHandle PointHandlePrefab => _pointHandlePrefab;

		/// <summary>
		/// Parent transform for spawned points.
		/// </summary>
		public Transform PointsParent => _pointsParent;

		/// <summary>
		/// Safety radius used for collision checks (meters).
		/// </summary>
		public float DroneRadius => _droneRadius;

		/// <summary>
		/// Layer mask that defines environment obstacles for collision checks.
		/// </summary>
		public LayerMask EnvironmentLayerMask => _environmentLayer;

	/// <summary>
	/// Currently selected waypoint type for new placements.
	/// </summary>
	public WaypointType CurrentTypeSelection
	{
		get => _currentTypeSelection;
		set
		{
			if (_currentTypeSelection != value)
			{
				Debug.Log($"PointPlacementManager: Changing waypoint type from {_currentTypeSelection} to {value}");
				_currentTypeSelection = value;
				UpdateGhostColorForType();
				Debug.Log($"PointPlacementManager: Ghost color updated to {WaypointTypeDefinition.GetTypeColor(value)}");
			}
		}
	}

	/// <summary>
	/// Whether we're currently adjusting the recording height for a Record360 waypoint.
	/// </summary>
	public bool IsAdjustingRecordingHeight => _recordPlacementState == RecordPlacementState.AdjustingHeight;

	/// <summary>
	/// Update the recording point position during height adjustment.
	/// Called by RayDepthController with the current ray information.
	/// </summary>
	public void UpdateRecordingPointFromRay(Vector3 rayOrigin, Vector3 rayDirection)
	{
		if (_recordPlacementState != RecordPlacementState.AdjustingHeight) return;
		if (_recordingHeightController == null) return;

		_recordingHeightController.UpdateRecordingPointFromRay(rayOrigin, rayDirection);
	}

	/// <summary>
	/// Update the recording point height directly.
	/// Called by RayDepthController when adjusting depth with stick.
	/// </summary>
	public void UpdateRecordingPointHeight(float targetY)
	{
		if (_recordPlacementState != RecordPlacementState.AdjustingHeight) return;
		if (_recordingHeightController == null) return;

		_recordingHeightController.UpdateRecordingPointHeight(targetY);
	}

	private void Awake()
	{
		if (_ghostRenderer == null)
		{
			var tr = _ghostTransform != null ? _ghostTransform.GetComponentInChildren<Renderer>() : null;
			if (tr != null) _ghostRenderer = tr;
		}
		
		// Thesis Feature: Initialize ghost with default type color
		UpdateGhostColorForType();

		// Record360 Feature: Initialize recording height controller
		if (_recordingHeightController == null)
		{
			_recordingHeightController = gameObject.AddComponent<RecordingHeightController>();
		}

		// Ensure recording ghost has a renderer if not set
		if (_recordingGhostRenderer == null && _recordingGhostTransform != null)
		{
			_recordingGhostRenderer = _recordingGhostTransform.GetComponentInChildren<Renderer>();
			if (_recordingGhostRenderer != null)
			{
				Debug.Log("PointPlacementManager: Auto-found recording ghost renderer");
			}
		}

		// Ensure anchor ghost has a renderer if not set
		if (_anchorGhostRenderer == null && _anchorGhostTransform != null)
		{
			_anchorGhostRenderer = _anchorGhostTransform.GetComponentInChildren<Renderer>();
			if (_anchorGhostRenderer != null)
			{
				Debug.Log("PointPlacementManager: Auto-found anchor ghost renderer");
			}
		}

		if (_recordingHeightController != null)
		{
			if (_recordingGhostTransform == null)
			{
				Debug.LogError("PointPlacementManager: Recording Ghost Transform not assigned! Please assign it in the Inspector.");
			}
			else
			{
				_recordingHeightController.SetRecordingGhostTransform(_recordingGhostTransform, _recordingGhostRenderer);
				_recordingHeightController.SetEnvironmentLayer(_environmentLayer);
				Debug.Log($"PointPlacementManager: Recording height controller initialized with ghost at {_recordingGhostTransform.position}");
			}
		}

		// Initially hide anchor ghost
		if (_anchorGhostTransform != null)
		{
			_anchorGhostTransform.gameObject.SetActive(false);
		}
		
		// Safety check: make sure anchor and recording ghosts are different objects
		if (_anchorGhostTransform != null && _recordingGhostTransform != null)
		{
			if (_anchorGhostTransform == _recordingGhostTransform)
			{
				Debug.LogError("ERROR: AnchorGhost and RecordingGhost are THE SAME object! They must be different. Please create two separate ghost objects.");
			}
			else
			{
				Debug.Log($"Ghosts configured correctly: Anchor={_anchorGhostTransform.name}, Recording={_recordingGhostTransform.name}");
			}
		}
	}

	/// <summary>
	/// Place a new point at the current ghost position and register it.
	/// For Record360 waypoints, this initiates the two-step placement process.
	/// </summary>
	public void PlaceAtCurrentGhost()
	{
		if (_ghostTransform == null || _pointHandlePrefab == null)
		{
			Debug.LogWarning("PointPlacementManager: Missing ghost transform or point handle prefab.");
			return;
		}

		// Record360 Feature: Handle two-step placement
		if (_currentTypeSelection == WaypointType.Record360)
		{
			HandleRecord360Placement();
			return;
		}

		// Standard placement for non-Record360 waypoints
		PlaceStandardWaypoint();
	}

	/// <summary>
	/// Handle the two-step placement flow for Record360 waypoints.
	/// </summary>
	private void HandleRecord360Placement()
	{
		Debug.LogError($"=== HandleRecord360Placement called, state={_recordPlacementState} ===");
		
		if (_recordPlacementState == RecordPlacementState.None)
		{
			Debug.LogError("=== STEP 1: PLACING ANCHOR POINT ===");
			// Step 1: Place anchor point
			Vector3 anchorPosition = _ghostTransform.position;
			var experimentManager = Experiment.ExperimentDataManager.Instance;

			// Check collision for anchor point
			if (CheckGhostCollisionWithObstacles())
			{
				Debug.LogWarning("PointPlacementManager: Cannot place anchor point in collision zone.");
				if (experimentManager != null)
				{
					experimentManager.OnPlacementBlocked(anchorPosition, "NoFlyZone");
				}
				return;
			}

		// Store anchor position and yaw
		_pendingAnchorPosition = anchorPosition;
		_pendingAnchorYaw = _rightHandRayOrigin != null ? _rightHandRayOrigin.eulerAngles.y : 0f;

		// Show anchor ghost at the anchor position (keep current color)
		if (_anchorGhostTransform != null)
		{
			_anchorGhostTransform.position = anchorPosition;
			_anchorGhostTransform.gameObject.SetActive(true);
			
			Debug.LogError($"=== AnchorGhost Name: {_anchorGhostTransform.name}, Children: {_anchorGhostTransform.childCount} ===");
			
			// Hide any distance labels on the anchor ghost
			Debug.LogError("=== ATTEMPTING TO DISABLE ANCHOR GHOST LABELS ===");
			DisableLabelsOnGhost(_anchorGhostTransform);
			Debug.LogError("=== FINISHED DISABLING ANCHOR GHOST LABELS ===");
			
			Debug.LogError($"AnchorGhost shown at {anchorPosition}, Active={_anchorGhostTransform.gameObject.activeSelf}");
		}
		else
		{
			Debug.LogError("ERROR: AnchorGhost transform is NULL! Not assigned in Inspector!");
		}
		
		// ALSO: Make absolutely sure the main depth readout is hidden
		if (_depthReadout != null)
		{
			Debug.LogError($"=== Main DepthReadout exists, hiding it. Name: {_depthReadout.gameObject.name} ===");
			_depthReadout.gameObject.SetActive(false);
		}
		else
		{
			Debug.LogError("=== Main DepthReadout is NULL ===");
		}

		// Activate recording height controller
		_recordPlacementState = RecordPlacementState.AdjustingHeight;
		if (_recordingHeightController != null)
		{
			_recordingHeightController.ActivateAt(anchorPosition);
		}

		// Hide the main ghost while adjusting height
		if (_ghostTransform != null)
		{
			_ghostTransform.gameObject.SetActive(false);
		}
		
		// Hide the main depth readout during Record360 adjustment
		if (_depthReadout != null)
		{
			_depthReadout.gameObject.SetActive(false);
		}

		Debug.Log($"Record360 Anchor placed at {anchorPosition}. Adjust recording height and press Enter to confirm.");
		}
		else if (_recordPlacementState == RecordPlacementState.AdjustingHeight)
		{
			Debug.LogError("=== STEP 2: CONFIRMING RECORDING HEIGHT ===");
			// Step 2: Confirm recording height and create waypoint
			ConfirmRecord360Placement();
		}
	}

	/// <summary>
	/// Confirm the Record360 placement with both anchor and recording positions.
	/// </summary>
	private void ConfirmRecord360Placement()
	{
		if (_recordingHeightController == null)
		{
			Debug.LogError("PointPlacementManager: Recording height controller not found!");
			CancelRecord360Placement();
			return;
		}

		Vector3 recordingPosition = _recordingHeightController.RecordingPosition;
		int id = _nextId++;

		// Create the point handle at the anchor position
		Color color = WaypointTypeDefinition.GetTypeColor(WaypointType.Record360);
		float radius = _placedPointRadius;

		Transform parent = _pointsParent != null ? _pointsParent : transform;
		PointHandle handle = Instantiate(_pointHandlePrefab, _pendingAnchorPosition, Quaternion.identity, parent);
		handle.Initialize(id, color, radius, this, WaypointType.Record360);

		// Set the recording position on the handle
		handle.SetRecordingPosition(recordingPosition);
		
		// Create a PERMANENT copy of the anchor ghost for this waypoint
		if (_anchorGhostTransform != null)
		{
			GameObject anchorCopy = Instantiate(_anchorGhostTransform.gameObject, _pendingAnchorPosition, Quaternion.identity, handle.transform);
			anchorCopy.name = $"AnchorVisual_{id}";
			anchorCopy.SetActive(true);
			
			// Make sure it's visible
			Renderer[] renderers = anchorCopy.GetComponentsInChildren<Renderer>(true);
			foreach (Renderer r in renderers)
			{
				r.enabled = true;
				r.gameObject.SetActive(true);
			}
			
			Debug.Log($"Created permanent anchor visual copy for waypoint {id}");
		}
		
		Debug.Log($"PointHandle {id} created at {_pendingAnchorPosition}");

		// Store in data structures
		var data = new PointData
		{
			Id = id,
			Position = _pendingAnchorPosition,
			Color = color,
			Radius = radius,
			CreatedAt = DateTime.UtcNow,
			Type = WaypointType.Record360,
			YawDegrees = _pendingAnchorYaw,
			Parameters = WaypointTypeDefinition.GetDefaultParameters(WaypointType.Record360),
			
			// ArduPilot/PX4 Integration: Indoor flight defaults for Record360
			AcceptanceRadius = 0.25f,  // 25cm precision for indoor flight
			HoldTime = 15.0f,          // Hold during recording
			SpeedMS = 0.3f             // Very slow approach for recording
		};

		// Store recording height in parameters
		data.Parameters["recording_height"] = recordingPosition.y;
		data.Parameters["recording_position"] = recordingPosition;

		_points.Add(data);
		_idToHandle[id] = handle;

		OnPointPlaced?.Invoke(data);

		// Notify experiment tracker
		var experimentManager = Experiment.ExperimentDataManager.Instance;
		if (experimentManager != null)
		{
			experimentManager.OnWaypointPlaced(_pendingAnchorPosition, ConvertToPointType(WaypointType.Record360), _pendingAnchorYaw);
		}

		// Deactivate recording height controller (this hides vertical line and recording ghost)
		_recordingHeightController.SetActive(false);

		// Hide the ORIGINAL anchor ghost (a permanent copy was created for this waypoint)
		if (_anchorGhostTransform != null)
		{
			_anchorGhostTransform.gameObject.SetActive(false);
			Debug.Log("Original AnchorGhost hidden, permanent copy created for waypoint");
		}

		// Create a visual indicator at recording position
		CreateRecordingPositionIndicator(handle, recordingPosition);

		// Show the main ghost again for placing more waypoints
		if (_ghostTransform != null)
		{
			_ghostTransform.gameObject.SetActive(true);
		}
		
		// Re-enable the main depth readout
		if (_depthReadout != null)
		{
			_depthReadout.gameObject.SetActive(true);
		}

		// Reset state
		_recordPlacementState = RecordPlacementState.None;

		Debug.Log($"Record360 waypoint placed: Anchor={_pendingAnchorPosition}, Recording={recordingPosition}. AnchorGhost MUST be visible!");
	}

	/// <summary>
	/// Cancel the Record360 placement process.
	/// </summary>
	public void CancelRecord360Placement()
	{
		if (_recordPlacementState == RecordPlacementState.None) return;

		// Deactivate recording height controller
		if (_recordingHeightController != null)
		{
			_recordingHeightController.SetActive(false);
		}

		// Hide anchor ghost
		if (_anchorGhostTransform != null)
		{
			_anchorGhostTransform.gameObject.SetActive(false);
		}

		// Show the main ghost again
		if (_ghostTransform != null)
		{
			_ghostTransform.gameObject.SetActive(true);
		}
		
		// Re-enable the main depth readout
		if (_depthReadout != null)
		{
			_depthReadout.gameObject.SetActive(true);
		}

		// Reset state
		_recordPlacementState = RecordPlacementState.None;

		Debug.Log("Record360 placement cancelled.");
	}

	/// <summary>
	/// Create a visual indicator showing the recording position and connection to anchor.
	/// </summary>
	private void CreateRecordingPositionIndicator(PointHandle anchorHandle, Vector3 recordingPosition)
	{
		if (anchorHandle == null) return;

		// Create a small sphere at recording position
		GameObject recordingSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		recordingSphere.name = $"RecordingPoint_{anchorHandle.Id}";
		recordingSphere.transform.position = recordingPosition;
		recordingSphere.transform.localScale = Vector3.one * (_placedPointRadius * 0.6f); // Smaller than anchor
		recordingSphere.transform.SetParent(anchorHandle.transform);

		// Set bright red color (brighter than anchor)
		Renderer sphereRenderer = recordingSphere.GetComponent<Renderer>();
		if (sphereRenderer != null)
		{
			Color brightRed = WaypointTypeDefinition.GetTypeColor(WaypointType.Record360); // Original bright red
			foreach (var mat in sphereRenderer.materials)
			{
				if (mat != null && mat.HasProperty("_Color"))
				{
					mat.color = brightRed;
				}
			}
		}

		// Remove collider so it doesn't interfere with raycasts
		Collider sphereCollider = recordingSphere.GetComponent<Collider>();
		if (sphereCollider != null)
		{
			Destroy(sphereCollider);
		}

		// Create a thin grey line connecting anchor to recording point
		GameObject lineObj = new GameObject($"RecordingLine_{anchorHandle.Id}");
		lineObj.transform.SetParent(anchorHandle.transform);
		LineRenderer line = lineObj.AddComponent<LineRenderer>();
		
		line.useWorldSpace = true;
		line.positionCount = 2;
		line.SetPosition(0, anchorHandle.transform.position);
		line.SetPosition(1, recordingPosition);
		line.startWidth = 0.01f; // 1cm thin line
		line.endWidth = 0.01f;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;

		// Create grey material for line
		Material lineMat = new Material(Shader.Find("Sprites/Default"));
		lineMat.color = new Color(0.5f, 0.5f, 0.5f, 0.8f); // Grey, slightly transparent
		line.material = lineMat;

		Debug.Log($"Created recording position indicator: Anchor={anchorHandle.transform.position}, Recording={recordingPosition}");
	}

	/// <summary>
	/// Place a standard (non-Record360) waypoint.
	/// </summary>
	private void PlaceStandardWaypoint()
	{
		Vector3 position = _ghostTransform.position;
		var experimentManager = Experiment.ExperimentDataManager.Instance;

		// Thesis Feature: Prevent placement in collision zones
		if (CheckGhostCollisionWithObstacles())
		{
			Debug.LogWarning("PointPlacementManager: Cannot place waypoint in collision zone (too close to obstacle).");

			// Thesis Feature: Notify experiment tracker
			if (experimentManager != null)
			{
				experimentManager.OnPlacementBlocked(position, "NoFlyZone");
			}

			return;
		}

		int id = _nextId++;

		// Thesis Feature: Use type-specific color
		Color color = WaypointTypeDefinition.GetTypeColor(_currentTypeSelection);
		float radius = _placedPointRadius;

		PointHandle handle = Instantiate(_pointHandlePrefab, position, Quaternion.identity, _pointsParent != null ? _pointsParent : transform);
		handle.Initialize(id, color, radius, this, _currentTypeSelection); // Pass type directly

		// Thesis Feature: Calculate yaw from right controller forward direction
		float yaw = 0f;
		if (_rightHandRayOrigin != null)
		{
			yaw = _rightHandRayOrigin.eulerAngles.y;
		}

		var data = new PointData
		{
			Id = id,
			Position = position,
			Color = color,
			Radius = radius,
			CreatedAt = DateTime.UtcNow,
			Type = _currentTypeSelection,
			YawDegrees = yaw,
			Parameters = WaypointTypeDefinition.GetDefaultParameters(_currentTypeSelection),
			
			// ArduPilot/PX4 Integration: Indoor flight defaults
			AcceptanceRadius = 0.25f,  // 25cm precision for indoor flight
			HoldTime = _currentTypeSelection == WaypointType.Record360 ? 15.0f : 2.0f,
			SpeedMS = _currentTypeSelection == WaypointType.Record360 ? 0.3f : 0.5f
		};

		_points.Add(data);
		_idToHandle[id] = handle;

		OnPointPlaced?.Invoke(data);

		// Thesis Feature: Notify experiment tracker
		if (experimentManager != null)
		{
			experimentManager.OnWaypointPlaced(position, ConvertToPointType(_currentTypeSelection), yaw);
		}
	}

		/// <summary>
		/// Undo (remove) the most recently placed point, if any.
		/// </summary>
		public void UndoLast()
		{
			if (_points.Count == 0) return;
			PointData last = _points[_points.Count - 1];
			_points.RemoveAt(_points.Count - 1);
			if (_idToHandle.TryGetValue(last.Id, out PointHandle handle))
			{
				_idToHandle.Remove(last.Id);
				if (handle != null)
				{
					Destroy(handle.gameObject);
				}
			}
		}

		/// <summary>
		/// Remove a specific point by ID.
		/// </summary>
		public bool RemovePoint(int pointId)
		{
			// Find and remove from data list
			for (int i = _points.Count - 1; i >= 0; i--)
			{
				if (_points[i].Id == pointId)
				{
					_points.RemoveAt(i);
					break;
				}
			}

			// Remove from handle dictionary and destroy GameObject
			if (_idToHandle.TryGetValue(pointId, out PointHandle handle))
			{
				_idToHandle.Remove(pointId);
				if (handle != null)
				{
					// Thesis Feature: Remove waypoint from route (keep remaining segments)
					var pathManager = UnityEngine.Object.FindFirstObjectByType<FlightPathManager>();
					if (pathManager != null && pathManager.IsPointInRoute(pointId))
					{
						pathManager.RemoveWaypointFromRoute(pointId);
					}
					
					// Thesis Feature: Notify experiment tracker
					var experimentManager = Experiment.ExperimentDataManager.Instance;
					if (experimentManager != null)
					{
						experimentManager.OnWaypointDeleted(pointId);
					}

					Destroy(handle.gameObject);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Returns a read-only list of all placed points.
		/// </summary>
		public IReadOnlyList<PointData> GetPoints()
		{
			return _points;
		}

		/// <summary>
		/// Get the current handle instance for a point ID, or null if not found.
		/// </summary>
		public PointHandle GetPoint(int id)
		{
			_idToHandle.TryGetValue(id, out PointHandle handle);
			return handle;
		}

		internal void NotifyHovered(PointHandle handle, bool isHovered)
		{
			// Clear hover state from all points first
			foreach (var kvp in _idToHandle)
			{
				if (kvp.Value != null && kvp.Value != handle)
				{
					kvp.Value.SetHoveredState(false);
				}
			}

			// Set hover state on the target handle
			if (handle != null && isHovered)
			{
				handle.SetHoveredState(true);
			}

			OnPointHovered?.Invoke(handle, isHovered);
		}

		internal void NotifySelected(PointHandle handle)
		{
			OnPointSelected?.Invoke(handle);
		}

	internal void UpdateGhostVisualValidity(bool isValid)
	{
		if (_ghostRenderer == null || _ghostTransform == null) return;
		
		// Thesis Feature: Collision avoidance check
		bool hasCollision = CheckGhostCollisionWithObstacles();
		
		// Override validity if collision detected
		bool finalValidity = isValid && !hasCollision;
		
		// Thesis Feature: Use type-specific color, or grey if collision detected
		Color typeColor = WaypointTypeDefinition.GetTypeColor(_currentTypeSelection);
		Color target;
		
		if (hasCollision)
		{
			// Grey semi-transparent when in collision zone
			target = _collisionGhostColor;
		}
		else if (finalValidity)
		{
			// Full brightness when valid
			target = typeColor;
		}
		else
		{
			// Dim if invalid (other reasons like depth clamping)
			target = typeColor * 0.5f;
		}
		
		foreach (var mat in _ghostRenderer.sharedMaterials)
		{
			if (mat != null && mat.HasProperty("_Color"))
			{
				mat.color = target;
			}
		}
	}
	
	/// <summary>
	/// Check if the ghost sphere is within the drone radius of any obstacle.
	/// Highlights obstacles that are too close and returns true if collision detected.
	/// </summary>
	private bool CheckGhostCollisionWithObstacles()
	{
		if (_ghostTransform == null) return false;
		
		Vector3 ghostPos = _ghostTransform.position;
		bool hasCollision = false;
		
		// Find all colliders within drone radius
		Collider[] nearbyColliders = Physics.OverlapSphere(ghostPos, _droneRadius, _environmentLayer);
		
		// Track which obstacles should be highlighted this frame
		HashSet<ObstacleHighlighter> shouldBeHighlighted = new HashSet<ObstacleHighlighter>();
		
		foreach (Collider col in nearbyColliders)
		{
			// Calculate distance to closest point on collider surface
			Vector3 closestPoint = col.ClosestPoint(ghostPos);
			float distance = Vector3.Distance(ghostPos, closestPoint);
			
			// If within drone radius, this is a collision
			if (distance < _droneRadius)
			{
				hasCollision = true;
				
				// Get or add ObstacleHighlighter component
				ObstacleHighlighter highlighter = col.GetComponent<ObstacleHighlighter>();
				if (highlighter == null)
				{
					highlighter = col.gameObject.AddComponent<ObstacleHighlighter>();
				}
				
				// Mark for highlighting with drone radius
				highlighter.Highlight(_droneRadius);
				shouldBeHighlighted.Add(highlighter);
			}
		}
		
		// Unhighlight obstacles that are no longer in range
		foreach (var highlighter in _currentlyHighlightedObstacles)
		{
			if (highlighter != null && !shouldBeHighlighted.Contains(highlighter))
			{
				highlighter.Unhighlight();
			}
		}
		
		// Update tracked set
		_currentlyHighlightedObstacles.Clear();
		foreach (var highlighter in shouldBeHighlighted)
		{
			_currentlyHighlightedObstacles.Add(highlighter);
		}
		
		return hasCollision;
	}

		internal void UpdateReadout(string text)
		{
			if (_depthReadout != null)
			{
				_depthReadout.SetText(text);
			}
		}

		internal void FadeReadout()
		{
			if (_depthReadout != null)
			{
				_depthReadout.FadeOut();
			}
		}

		internal void TickHaptics(float amplitude = 0.2f, float duration = 0.02f)
		{
			if (_hapticsHelper != null)
			{
				_hapticsHelper.Tick(amplitude, duration);
			}
		}

		internal void ConfirmHaptics(float amplitude = 0.6f, float duration = 0.08f)
		{
			if (_hapticsHelper != null)
			{
				_hapticsHelper.Pulse(amplitude, duration);
			}
		}

		/// <summary>
		/// Update ghost sphere color to match currently selected waypoint type.
		/// </summary>
		private void UpdateGhostColorForType()
		{
			if (_ghostRenderer == null)
			{
				Debug.LogWarning("PointPlacementManager: Ghost renderer is null! Cannot update ghost color.");
				return;
			}
			
			Color typeColor = WaypointTypeDefinition.GetTypeColor(_currentTypeSelection);
			Debug.Log($"PointPlacementManager: Updating ghost color to {typeColor} for type {_currentTypeSelection}");
			
			foreach (var mat in _ghostRenderer.sharedMaterials)
			{
				if (mat != null && mat.HasProperty("_Color"))
				{
					mat.color = typeColor;
					Debug.Log($"PointPlacementManager: Set material color to {typeColor}");
				}
			}
		}

		/// <summary>
		/// Get the data for a specific point by ID.
		/// </summary>
		public PointData? GetPointData(int id)
		{
			foreach (var point in _points)
			{
				if (point.Id == id)
				{
					return point;
				}
			}
			return null;
		}
		
		/// <summary>
		/// Convert WaypointType to Experiment.PointType for tracking.
		/// </summary>
		private Experiment.PointType ConvertToPointType(WaypointType waypointType)
		{
			switch (waypointType)
			{
				case WaypointType.StopTurnGo:
					return Experiment.PointType.StopAndRotate;
				case WaypointType.Record360:
					return Experiment.PointType.Record360;
				default:
					return Experiment.PointType.StopAndRotate;
			}
		}
		
	/// <summary>
	/// Disable any PointLabelBillboard components on a ghost to prevent distance labels from showing.
	/// This is needed because recording/anchor ghosts are duplicates of the main ghost.
	/// FIXED: Disables the "Depth Readout" child GameObject by name.
	/// </summary>
	private void DisableLabelsOnGhost(Transform ghostTransform)
	{
		if (ghostTransform == null)
		{
			Debug.LogError("DisableLabelsOnGhost: ghostTransform is NULL!");
			return;
		}
		
		Debug.LogError($"PointPlacementManager: Scanning {ghostTransform.name} for labels...");
		
		// SOLUTION: Find and disable the "Depth Readout" child by name
		Transform depthReadoutChild = ghostTransform.Find("Depth Readout");
		if (depthReadoutChild != null)
		{
			depthReadoutChild.gameObject.SetActive(false);
			Debug.LogError($"✅ DISABLED 'Depth Readout' child on {ghostTransform.name}!");
		}
		else
		{
			Debug.LogError($"⚠️ No 'Depth Readout' child found on {ghostTransform.name}");
		}
	}
	}
}



