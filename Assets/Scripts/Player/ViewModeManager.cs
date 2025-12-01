using Unity.XR.CoreUtils;
using UnityEngine;
using Experiment;

namespace Player
{
	/// <summary>
	/// Manages view mode setup based on experiment TaskVariant.
	/// Automatically configures RoomView or BirdsEyeView at startup.
	/// </summary>
	public class ViewModeManager : MonoBehaviour
	{
		[Header("XR Setup")]
		[SerializeField] private XROrigin _xrOrigin;
		
		[Header("Environment References")]
		[Tooltip("The ground plane/table that environments sit on")]
		[SerializeField] private Transform _groundPlane;
		
		[Tooltip("Center point of the environment (corridor or staircase)")]
		[SerializeField] private Transform _environmentCenter;
		
		[Header("Room View Anchors")]
		[SerializeField] private Transform _roomViewAnchor_Corridor;
		[SerializeField] private Transform _roomViewAnchor_Staircase;
		
		[Header("Birds-Eye View Settings")]
		[Tooltip("Height above ground plane for birds-eye view")]
		[SerializeField] private float _birdsEyeHeight = 3.0f;
		
		[Tooltip("Distance from environment center (horizontal)")]
		[SerializeField] private float _birdsEyeDistance = 4.0f;
		
		[Tooltip("Downward angle in degrees (30° = looking down at model)")]
		[SerializeField] private float _birdsEyePitchDegrees = 30f;
		
		[Header("Birds-Eye Locomotion Constraints")]
		[Tooltip("Minimum height above ground (prevents dropping into rooms)")]
		[SerializeField] private float _minHeight = 1.5f;
		
		[Tooltip("Maximum height above ground")]
		[SerializeField] private float _maxHeight = 6.0f;
		
		[Tooltip("Minimum distance from environment center")]
		[SerializeField] private float _minRadius = 2.0f;
		
		[Tooltip("Maximum distance from environment center")]
		[SerializeField] private float _maxRadius = 8.0f;
		
		[Header("Locomotion Components")]
		[SerializeField] private MonoBehaviour _roomViewLocomotion; // VRPlayerController or similar
		[SerializeField] private BirdsEyeLocomotion _birdsEyeLocomotion;
		
		private ViewMode _currentViewMode;
		private TaskVariant _taskVariant;
		
		public enum ViewMode
		{
			RoomView,
			BirdsEyeView
		}
		
		private void Awake()
		{
			if (_xrOrigin == null)
			{
				_xrOrigin = FindFirstObjectByType<XROrigin>();
			}
			
			// Get TaskVariant from ExperimentDataManager
			var experimentManager = ExperimentDataManager.Instance;
			if (experimentManager != null)
			{
				_taskVariant = experimentManager.taskVariant;
			}
			else
			{
				Debug.LogWarning("ViewModeManager: ExperimentDataManager not found. Defaulting to RoomView.");
				_taskVariant = TaskVariant.RoomView_Corridor;
			}
			
			// Determine view mode from TaskVariant
			_currentViewMode = DetermineViewMode(_taskVariant);
			
			// Auto-find ground plane if not set
			if (_groundPlane == null)
			{
				GameObject planeObj = GameObject.Find("Plane");
				if (planeObj != null)
				{
					_groundPlane = planeObj.transform;
				}
			}
			
			// Auto-find environment center if not set
			if (_environmentCenter == null)
			{
				// Try to find by name
				GameObject envObj = GameObject.Find("18.11.2025 Corridor");
				if (envObj == null)
				{
					envObj = GameObject.Find("28.11.2025 L shape");
				}
				if (envObj == null)
				{
					envObj = GameObject.Find("27.11.2025 staircase");
				}
				
				if (envObj != null)
				{
					_environmentCenter = envObj.transform;
				}
				else
				{
					// Fallback: use ground plane center
					_environmentCenter = _groundPlane;
				}
			}
		}
		
		private void Start()
		{
			// Disable PlayerSpawnPoint if it exists (we handle positioning)
			var playerSpawnPoint = FindFirstObjectByType<PlayerSpawnPoint>();
			if (playerSpawnPoint != null && _currentViewMode == ViewMode.BirdsEyeView)
			{
				playerSpawnPoint.enabled = false;
				Debug.Log("[ViewModeManager] Disabled PlayerSpawnPoint - ViewModeManager handling positioning.");
			}
			
			// Wait for XR to initialize, then set up view
			StartCoroutine(SetupViewAfterXRInit());
		}
		
		private System.Collections.IEnumerator SetupViewAfterXRInit()
		{
			// Wait for XR system to fully initialize
			for (int i = 0; i < 3; i++)
			{
				yield return null;
			}
			
			// Apply the determined view mode
			ApplyViewMode(_currentViewMode);
		}
		
		private ViewMode DetermineViewMode(TaskVariant variant)
		{
			switch (variant)
			{
				case TaskVariant.RoomView_Corridor:
				case TaskVariant.RoomView_Staircase:
					return ViewMode.RoomView;
					
				case TaskVariant.BirdsEye_Corridor:
				case TaskVariant.BirdsEye_Staircase:
					return ViewMode.BirdsEyeView;
					
				default:
					return ViewMode.RoomView;
			}
		}
		
		private void ApplyViewMode(ViewMode mode)
		{
			_currentViewMode = mode;
			
			switch (mode)
			{
				case ViewMode.RoomView:
					SetupRoomView();
					break;
					
				case ViewMode.BirdsEyeView:
					SetupBirdsEyeView();
					break;
			}
			
			// Enable/disable locomotion components
			UpdateLocomotionComponents();
		}
		
		private void SetupRoomView()
		{
			Transform anchor = GetRoomViewAnchor();
			
			if (anchor != null && _xrOrigin != null)
			{
				// Use existing PlayerSpawnPoint logic
				Vector3 targetCameraPos = anchor.position;
				
				Transform cameraTransform = _xrOrigin.Camera.transform;
				if (_xrOrigin.CameraFloorOffsetObject != null)
				{
					cameraTransform = _xrOrigin.CameraFloorOffsetObject.transform;
				}
				
				Vector3 cameraLocalPos = _xrOrigin.transform.InverseTransformPoint(cameraTransform.position);
				float cameraLocalY = cameraLocalPos.y;
				
				targetCameraPos.y -= cameraLocalY;
				
				_xrOrigin.MoveCameraToWorldLocation(targetCameraPos);
				_xrOrigin.MatchOriginUpCameraForward(Vector3.up, anchor.forward);
				
				Debug.Log($"[ViewModeManager] RoomView setup complete. Position: {targetCameraPos}");
			}
			else
			{
				// Fallback: let PlayerSpawnPoint handle it if anchor not found
				Debug.LogWarning("[ViewModeManager] RoomView anchor not found. PlayerSpawnPoint will handle positioning.");
			}
		}
		
		private void SetupBirdsEyeView()
		{
			if (_xrOrigin == null || _environmentCenter == null)
			{
				Debug.LogError("[ViewModeManager] Cannot setup BirdsEyeView: Missing XR Origin or environment center.");
				return;
			}
			
			// Calculate position above and offset from environment center
			Vector3 envCenter = _environmentCenter.position;
			float groundY = _groundPlane != null ? _groundPlane.position.y : envCenter.y;
			
			// Position: above ground, offset from center
			Vector3 targetPosition = new Vector3(
				envCenter.x + _birdsEyeDistance,
				groundY + _birdsEyeHeight,
				envCenter.z
			);
			
			// Calculate rotation: look down at environment center with specified pitch
			Vector3 directionToCenter = (envCenter - targetPosition).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToCenter, Vector3.up);
			
			// Apply pitch adjustment
			targetRotation = targetRotation * Quaternion.Euler(_birdsEyePitchDegrees, 0, 0);
			
			// Move XR Origin
			Transform cameraTransform = _xrOrigin.Camera.transform;
			if (_xrOrigin.CameraFloorOffsetObject != null)
			{
				cameraTransform = _xrOrigin.CameraFloorOffsetObject.transform;
			}
			
			Vector3 cameraLocalPos = _xrOrigin.transform.InverseTransformPoint(cameraTransform.position);
			float cameraLocalY = cameraLocalPos.y;
			
			Vector3 targetCameraPos = targetPosition;
			targetCameraPos.y -= cameraLocalY;
			
			_xrOrigin.MoveCameraToWorldLocation(targetCameraPos);
			_xrOrigin.MatchOriginUpCameraForward(Vector3.up, targetRotation * Vector3.forward);
			
			Debug.Log($"[ViewModeManager] BirdsEyeView setup complete. Position: {targetPosition}, Looking at: {envCenter}");
		}
		
		private Transform GetRoomViewAnchor()
		{
			// Determine which anchor based on TaskVariant
			if (_taskVariant == TaskVariant.RoomView_Corridor || _taskVariant == TaskVariant.BirdsEye_Corridor)
			{
				return _roomViewAnchor_Corridor;
			}
			else if (_taskVariant == TaskVariant.RoomView_Staircase || _taskVariant == TaskVariant.BirdsEye_Staircase)
			{
				return _roomViewAnchor_Staircase;
			}
			
			// Fallback to corridor anchor
			return _roomViewAnchor_Corridor ?? _roomViewAnchor_Staircase;
		}
		
		private void UpdateLocomotionComponents()
		{
			// Enable/disable room view locomotion
			if (_roomViewLocomotion != null)
			{
				_roomViewLocomotion.enabled = (_currentViewMode == ViewMode.RoomView);
			}
			
			// Ensure birds-eye locomotion exists and is configured
			if (_currentViewMode == ViewMode.BirdsEyeView)
			{
				if (_birdsEyeLocomotion == null && _xrOrigin != null)
				{
					_birdsEyeLocomotion = _xrOrigin.GetComponent<BirdsEyeLocomotion>();
					if (_birdsEyeLocomotion == null)
					{
						_birdsEyeLocomotion = _xrOrigin.gameObject.AddComponent<BirdsEyeLocomotion>();
						Debug.Log("[ViewModeManager] Auto-added BirdsEyeLocomotion to XR Origin.");
					}
				}
				
				if (_birdsEyeLocomotion != null)
				{
					_birdsEyeLocomotion.enabled = true;
					_birdsEyeLocomotion.SetConstraints(_minHeight, _maxHeight, _minRadius, _maxRadius, _environmentCenter);
				}
			}
			else
			{
				// Disable birds-eye locomotion in room view
				if (_birdsEyeLocomotion != null)
				{
					_birdsEyeLocomotion.enabled = false;
				}
			}
		}
		
		/// <summary>
		/// Get the current view mode (read-only).
		/// </summary>
		public ViewMode CurrentViewMode => _currentViewMode;
	}
}

